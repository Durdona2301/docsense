using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace DocSense.Web;

public sealed class DocumentStore(NpgsqlDataSource db)
{
    public async Task Initialize(CancellationToken ct)
    {
        await using var command = db.CreateCommand("""
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE TABLE IF NOT EXISTS documents (
              id uuid PRIMARY KEY, name text NOT NULL, kind text NOT NULL, bytes bigint NOT NULL,
              hash text NOT NULL UNIQUE, created_at timestamptz NOT NULL DEFAULT now());
            CREATE TABLE IF NOT EXISTS chunks (
              id uuid PRIMARY KEY, document_id uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
              ordinal integer NOT NULL, page integer, content text NOT NULL,
              search tsvector GENERATED ALWAYS AS (to_tsvector('simple', content)) STORED,
              embedding vector, embedding_model text, UNIQUE(document_id, ordinal));
            CREATE INDEX IF NOT EXISTS chunks_search ON chunks USING gin(search);
            CREATE INDEX IF NOT EXISTS chunks_document ON chunks(document_id);
            """);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> Healthy(CancellationToken ct)
    {
        try
        {
            await using var cmd = db.CreateCommand("SELECT 1");
            return (int)(await cmd.ExecuteScalarAsync(ct))! == 1;
        }
        catch (NpgsqlException) { return false; }
    }

    public async Task<List<DocumentInfo>> List(CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("""
            SELECT d.id, d.name, d.kind, d.bytes, count(c.id)::int,
                   count(c.embedding)::int, d.created_at
            FROM documents d LEFT JOIN chunks c ON c.document_id=d.id
            GROUP BY d.id ORDER BY d.created_at DESC, d.id
            """);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        List<DocumentInfo> result = [];
        while (await reader.ReadAsync(ct)) result.Add(ReadInfo(reader));
        return result;
    }

    public async Task<DocumentInfo?> FindHash(string hash, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("""
            SELECT d.id, d.name, d.kind, d.bytes, count(c.id)::int, count(c.embedding)::int, d.created_at
            FROM documents d LEFT JOIN chunks c ON c.document_id=d.id WHERE d.hash=$1 GROUP BY d.id
            """);
        cmd.Parameters.AddWithValue(hash);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadInfo(reader) : null;
    }

    public async Task<UploadResult> Insert(Guid id, string name, long bytes, string hash,
        IReadOnlyList<DocumentChunk> chunks, float[][]? embeddings, string model, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // Serialize quota checks and duplicate inserts across app processes.
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(624819301)", connection, tx))
            await gate.ExecuteNonQueryAsync(ct);
        await using (var quota = new NpgsqlCommand("SELECT count(*) FROM documents", connection, tx))
            if ((long)(await quota.ExecuteScalarAsync(ct))! >= 200)
                throw new InputException("This local workspace holds up to 200 documents. Remove a document first.", 409);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO documents(id,name,kind,bytes,hash) VALUES($1,$2,$3,$4,$5)
            ON CONFLICT(hash) DO NOTHING RETURNING created_at
            """, connection, tx);
        insert.Parameters.AddWithValue(id);
        insert.Parameters.AddWithValue(name);
        insert.Parameters.AddWithValue(Path.GetExtension(name).TrimStart('.').ToUpperInvariant());
        insert.Parameters.AddWithValue(bytes);
        insert.Parameters.AddWithValue(hash);
        var created = await insert.ExecuteScalarAsync(ct);
        if (created is null)
        {
            await tx.RollbackAsync(ct);
            return new((await FindHash(hash, ct))!, true, "This document is already in your library.");
        }
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO chunks(id,document_id,ordinal,page,content,embedding,embedding_model)
                VALUES($1,$2,$3,$4,$5,$6::vector,$7)
                """, connection, tx);
            cmd.Parameters.AddWithValue(chunk.Id);
            cmd.Parameters.AddWithValue(id);
            cmd.Parameters.AddWithValue(chunk.Ordinal);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Integer, (object?)chunk.Page ?? DBNull.Value);
            cmd.Parameters.AddWithValue(chunk.Content);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Text, embeddings is null ? DBNull.Value : Vector(embeddings[i]));
            cmd.Parameters.AddWithValue(NpgsqlDbType.Text, embeddings is null ? DBNull.Value : model);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(new(id, name, Path.GetExtension(name).TrimStart('.').ToUpperInvariant(), bytes,
            chunks.Count, embeddings?.Length ?? 0, new DateTimeOffset((DateTime)created)), false, null);
    }

    public async Task<List<Source>> Search(string question, Guid[] ids, float[]? vector, string model, CancellationToken ct)
    {
        var query = TextProcessing.Query(question);
        if (query.Length == 0 && vector is null) return [];
        // Exact vector scan is deliberate for a bounded personal corpus. Different model dimensions never mix.
        await using var cmd = db.CreateCommand("""
            WITH candidates AS (
              SELECT c.*, d.name,
                ts_rank_cd(c.search, to_tsquery('simple', $1)) AS lexical,
                CASE WHEN c.embedding_model=$4 AND vector_dims(c.embedding)=vector_dims($3::vector)
                     THEN 1-(c.embedding <=> $3::vector) ELSE 0 END AS semantic
              FROM chunks c JOIN documents d ON d.id=c.document_id
              WHERE cardinality($2::uuid[])=0 OR c.document_id=ANY($2)
            )
            SELECT id,document_id,name,ordinal,page,content,
                   (lexical / (lexical + 0.1) + greatest(semantic, 0))::double precision AS score
            FROM candidates WHERE lexical>0 OR semantic>=0.60
            ORDER BY score DESC, id LIMIT 5
            """);
        cmd.Parameters.AddWithValue(query);
        cmd.Parameters.AddWithValue(ids);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Text, vector is null ? DBNull.Value : Vector(vector));
        cmd.Parameters.AddWithValue(model);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        List<Source> sources = [];
        while (await reader.ReadAsync(ct))
            sources.Add(new(sources.Count + 1, reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.GetString(5), reader.GetDouble(6)));
        return sources;
    }

    public async Task<List<DocumentChunk>> Chunks(Guid id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("SELECT id,document_id,ordinal,page,content FROM chunks WHERE document_id=$1 ORDER BY ordinal");
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        List<DocumentChunk> chunks = [];
        while (await reader.ReadAsync(ct)) chunks.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetString(4)));
        return chunks;
    }

    public async Task<bool> Delete(Guid id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("DELETE FROM documents WHERE id=$1");
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> Reindex(Guid id, IReadOnlyList<DocumentChunk> chunks, float[][] vectors, string model, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var exists = new NpgsqlCommand("SELECT id FROM documents WHERE id=$1 FOR UPDATE", connection, tx);
        exists.Parameters.AddWithValue(id);
        if (await exists.ExecuteScalarAsync(ct) is null) return false;
        for (var i = 0; i < chunks.Count; i++)
        {
            await using var cmd = new NpgsqlCommand("UPDATE chunks SET embedding=$1::vector,embedding_model=$2 WHERE id=$3", connection, tx);
            cmd.Parameters.AddWithValue(Vector(vectors[i]));
            cmd.Parameters.AddWithValue(model);
            cmd.Parameters.AddWithValue(chunks[i].Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }

    private static string Vector(float[] values) => "[" + string.Join(',', values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";
    private static DocumentInfo ReadInfo(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2),
        r.GetInt64(3), r.GetInt32(4), r.GetInt32(5), r.GetFieldValue<DateTimeOffset>(6));
}

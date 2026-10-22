using System.Security.Cryptography;
using System.Text.Json;

namespace DocSense.Web;

public sealed class Workspace(DocumentStore store, OllamaClient ai, ILogger<Workspace> logger)
{
    private static readonly SemaphoreSlim ImportGate = new(1, 1);

    public async Task<UploadResult> Import(string filename, byte[] bytes, CancellationToken ct)
    {
        var name = TextProcessing.SafeName(filename);
        await ImportGate.WaitAsync(ct);
        try
        {
            var pages = TextProcessing.Extract(name, bytes, ct);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var existing = await store.FindHash(hash, ct);
            if (existing is not null) return new(existing, true, "This document is already in your library.");
            var id = Guid.NewGuid();
            var chunks = TextProcessing.Chunk(id, pages);
            var (vectors, notice) = await TryEmbed(chunks.Select(c => c.Content).ToArray(), ct);
            var result = await store.Insert(id, name, bytes.Length, hash, chunks, vectors, ai.ModelKey, ct);
            return result with { Notice = result.Notice ?? notice };
        }
        finally { ImportGate.Release(); }
    }

    public async Task Reindex(Guid id, CancellationToken ct)
    {
        if (!ai.Enabled) throw new InputException("Enable Ollama in configuration before creating embeddings.", 409);
        await ImportGate.WaitAsync(ct);
        try
        {
            var chunks = await store.Chunks(id, ct);
            if (chunks.Count == 0) throw new InputException("This document no longer exists.", 404);
            var (vectors, _) = await TryEmbed(chunks.Select(c => c.Content).ToArray(), ct);
            if (vectors is null) throw new InputException("Ollama is unavailable. Check the service and embedding model, then retry.", 503);
            if (!await store.Reindex(id, chunks, vectors, ai.ModelKey, ct))
                throw new InputException("This document no longer exists.", 404);
        }
        finally { ImportGate.Release(); }
    }

    public async Task<Answer> Ask(AskRequest request, CancellationToken ct)
    {
        var question = request.Question?.Trim() ?? "";
        if (question.Length is < 3 or > 1500) throw new InputException("Ask a question between 3 and 1,500 characters.");
        var ids = request.DocumentIds?.Distinct().ToArray() ?? [];
        if (ids.Length > 200) throw new InputException("Select up to 200 documents.");
        var documents = await store.List(ct);
        if (ids.Any(id => documents.All(d => d.Id != id))) throw new InputException("A selected document no longer exists. Refresh your library.", 404);
        if (documents.Count == 0) throw new InputException("Upload a document or load the sample library first.", 409);
        var (vectors, notice) = await TryEmbed([question], ct);
        var sources = await store.Search(question, ids, vectors?.FirstOrDefault(), ai.ModelKey, ct);
        if (sources.Count == 0) return NoEvidence(question, notice);
        if (ai.Enabled)
        {
            try
            {
                var result = await ai.Generate(question, sources, ct);
                if (result?.InsufficientEvidence == true) return NoEvidence(question);
                if (result is not null)
                    return new(question, result.Answer!, "generated",
                        sources.Where(s => result.Citations!.Contains(s.Number)).ToList(), notice);
                notice = "The model returned an answer without valid citations. Showing original passages instead.";
            }
            catch (Exception e) when (IsProviderFailure(e, ct))
            {
                logger.LogWarning("Local answer model unavailable ({FailureType}).", e.GetType().Name);
                notice = "Ollama could not generate an answer. Showing original passages instead.";
            }
        }
        return new(question, sources[0].Content, "extractive", sources,
            notice ?? "Original passage from your documents. No AI-generated answer.");
    }

    private async Task<(float[][]? Vectors, string? Notice)> TryEmbed(string[] texts, CancellationToken ct)
    {
        if (!ai.Enabled) return (null, null);
        try { return (await ai.Embed(texts, ct), null); }
        catch (Exception e) when (IsProviderFailure(e, ct))
        {
            logger.LogWarning("Local embedding model unavailable ({FailureType}).", e.GetType().Name);
            return (null, "Embeddings are unavailable. Keyword search is active; reindex after Ollama is ready.");
        }
    }

    private static bool IsProviderFailure(Exception e, CancellationToken ct) =>
        e is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException ||
        (e is OperationCanceledException && !ct.IsCancellationRequested);
    private static Answer NoEvidence(string question, string? notice = null) => new(question,
        "I couldn’t find enough evidence in your documents to answer this. Try different keywords or add a document covering this topic.",
        "no-evidence", [], notice);

    public static readonly (string Name, string Text)[] Samples =
    [
        ("Product handbook.md", """
            # Example product handbook

            This fictional sample is for trying DocSense. It does not describe DocSense pricing.

            ## Trial period
            The sample product includes a 14-day trial. No credit card is required to get started.
            All core features are available during the trial. After the trial, users can choose a paid plan.

            ## Data export
            Workspace owners can export their records as CSV from Settings > Data > Export.
            Exports include records and timestamps, but do not include uploaded attachments.

            ## Support
            Standard support is available Monday through Friday, from 09:00 to 17:00 UTC.
            """),
        ("API reference.txt", """
            Example API reference — fictional sample

            Authentication: send your workspace API key in the Authorization header using the Bearer scheme.
            Never put an API key in a URL query string.

            Rate limits: the sample API accepts 60 requests per minute per workspace.
            Exceeding this limit returns HTTP 429. Wait for the Retry-After header before retrying.

            Pagination: list endpoints accept a cursor and a limit between 1 and 100.
            """),
        ("Release notes.md", """
            # Example release notes

            This fictional sample describes version 1.2 of the example product.
            Version 1.2 adds CSV export, keyboard shortcuts and a 14-day trial for new workspaces.
            Existing workspaces keep their current plan. The update does not change stored records.
            """)
    ];
}

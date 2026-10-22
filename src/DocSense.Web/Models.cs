namespace DocSense.Web;

public sealed record DocumentInfo(Guid Id, string Name, string Kind, long Bytes, int ChunkCount,
    int EmbeddedChunks, DateTimeOffset CreatedAt);
public sealed record DocumentChunk(Guid Id, Guid DocumentId, int Ordinal, int? Page, string Content);
public sealed record Source(int Number, Guid ChunkId, Guid DocumentId, string DocumentName,
    int Ordinal, int? Page, string Content, double Score);
public sealed record AskRequest(string? Question, Guid[]? DocumentIds);
public sealed record Answer(string Question, string Text, string Mode, IReadOnlyList<Source> Sources, string? Notice = null);
public sealed record UploadResult(DocumentInfo Document, bool Duplicate, string? Notice);
public sealed record TextPage(int? Number, string Text);
public sealed class InputException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class AiOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "embeddinggemma";
    public string ChatModel { get; set; } = "gemma3:4b";
    public int Dimensions { get; set; } = 768;
}

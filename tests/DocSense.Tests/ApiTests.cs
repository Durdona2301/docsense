using System.Net;
using System.Net.Http.Json;
using System.Text;
using DocSense.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DocSense.Tests;

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCSENSE_TEST_DATABASE")))
            Skip = "Set DOCSENSE_TEST_DATABASE to a disposable PostgreSQL/pgvector server connection (CREATE DATABASE required).";
    }
}

public sealed class ApiFixture : IAsyncLifetime
{
    private string? admin;
    private string? database;
    private WebApplicationFactory<Program>? factory;
    public HttpClient Client { get; private set; } = null!;
    public DocumentStore Store => factory!.Services.GetRequiredService<DocumentStore>();
    public async Task InitializeAsync()
    {
        admin = Environment.GetEnvironmentVariable("DOCSENSE_TEST_DATABASE");
        if (string.IsNullOrEmpty(admin)) return;
        database = "docsense_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
        await cmd.ExecuteNonQueryAsync();
        var cs = new NpgsqlConnectionStringBuilder(admin) { Database = database };
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.AddSingleton(NpgsqlDataSource.Create(cs.ConnectionString));
            });
        });
        Client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost") });
        Client.DefaultRequestHeaders.Add("X-DocSense", "1");
    }
    public async Task DisposeAsync()
    {
        if (factory is not null) { Client.Dispose(); await factory.DisposeAsync(); }
        if (database is null) return;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", connection);
        await cmd.ExecuteNonQueryAsync();
    }
}

public sealed class ApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private HttpClient Client => fixture.Client;

    [IntegrationFact]
    public async Task UploadSearchScopeDuplicateDeleteLifecycle()
    {
        using var first = await Upload("handbook.md", "The cobalt trial lasts 14 days. Export records as CSV.");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var upload = (await first.Content.ReadFromJsonAsync<UploadResult>())!;
        Assert.Equal(1, upload.Document.ChunkCount);
        using var duplicate = await Upload("renamed.md", "The cobalt trial lasts 14 days. Export records as CSV.");
        var existing = (await duplicate.Content.ReadFromJsonAsync<UploadResult>())!;
        Assert.True(existing.Duplicate);
        Assert.Equal(upload.Document.Id, existing.Document.Id);

        using var answerResponse = await Client.PostAsJsonAsync("/api/ask", new AskRequest("How long is the cobalt trial?", [upload.Document.Id]));
        answerResponse.EnsureSuccessStatusCode();
        var answer = (await answerResponse.Content.ReadFromJsonAsync<Answer>())!;
        Assert.Equal("extractive", answer.Mode);
        Assert.Contains("14 days", answer.Text);
        Assert.All(answer.Sources, s => Assert.Equal(upload.Document.Id, s.DocumentId));

        using var unknown = await Client.PostAsJsonAsync("/api/ask", new AskRequest("Elephant migration in Antarctica?", [upload.Document.Id]));
        var abstention = (await unknown.Content.ReadFromJsonAsync<Answer>())!;
        Assert.Equal("no-evidence", abstention.Mode);
        Assert.Empty(abstention.Sources);

        using var chunks = await Client.GetAsync($"/api/documents/{upload.Document.Id}/chunks");
        Assert.Equal(HttpStatusCode.OK, chunks.StatusCode);
        using var remove = await Client.DeleteAsync($"/api/documents/{upload.Document.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        using var missing = await Client.GetAsync($"/api/documents/{upload.Document.Id}/chunks");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var staleScope = await Client.PostAsJsonAsync("/api/ask", new AskRequest("What is the trial?", [upload.Document.Id]));
        Assert.Equal(HttpStatusCode.NotFound, staleScope.StatusCode);
    }

    [IntegrationFact]
    public async Task WrongDocumentScopeDoesNotLeakAnotherDocument()
    {
        using var a = await Upload("one.txt", "Cedar membership lasts fifteen months.");
        using var b = await Upload("two.txt", "Olive shipping takes seven business days.");
        var doc = (await b.Content.ReadFromJsonAsync<UploadResult>())!.Document;
        using var response = await Client.PostAsJsonAsync("/api/ask", new AskRequest("Cedar membership?", [doc.Id]));
        var answer = (await response.Content.ReadFromJsonAsync<Answer>())!;
        Assert.Equal("no-evidence", answer.Mode);
        Assert.DoesNotContain("fifteen", answer.Text);
    }

    [IntegrationFact]
    public async Task SamplesAreIdempotent()
    {
        using var first = await Client.PostAsync("/api/samples", null);
        first.EnsureSuccessStatusCode();
        using var second = await Client.PostAsync("/api/samples", null);
        var results = (await second.Content.ReadFromJsonAsync<UploadResult[]>())!;
        Assert.Equal(3, results.Length);
        Assert.All(results, r => Assert.True(r.Duplicate));
    }

    [IntegrationFact]
    public async Task ValidatesFilesQuestionsAndCsrfHeader()
    {
        using var unsupported = await Upload("program.exe", "not a text document");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, unsupported.StatusCode);
        using var empty = await Upload("empty.txt", "");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, empty.StatusCode);
        using var invalidQuestion = await Client.PostAsJsonAsync("/api/ask", new AskRequest(" ", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalidQuestion.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/documents/{Guid.NewGuid()}");
        request.Headers.Add("X-DocSense", "0");
        using var blocked = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("frame-ancestors 'none'", blocked.Headers.GetValues("Content-Security-Policy").Single());
        using var reindex = await Client.PostAsync($"/api/documents/{Guid.NewGuid()}/reindex", null);
        Assert.Equal(HttpStatusCode.Conflict, reindex.StatusCode);
    }

    [IntegrationFact]
    public async Task ConcurrentDuplicateUploadsProduceOneDocument()
    {
        var text = $"Unique concurrent document {Guid.NewGuid()} with saffron details.";
        var results = await Task.WhenAll(Upload("a.md", text), Upload("b.md", text));
        var bodies = await Task.WhenAll(results.Select(r => r.Content.ReadFromJsonAsync<UploadResult>()));
        Assert.Single(bodies, b => !b!.Duplicate);
        Assert.Equal(bodies[0]!.Document.Id, bodies[1]!.Document.Id);
        foreach (var result in results) result.Dispose();
    }

    [IntegrationFact]
    public async Task RejectsUnexpectedHost()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/documents");
        request.Headers.Host = "untrusted.example";
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task VectorSearchFiltersModelAndDimensionsAndReindexIsAtomic()
    {
        var id = Guid.NewGuid();
        var chunk = new DocumentChunk(Guid.NewGuid(), id, 1, 2, "A passage about evergreen support.");
        await fixture.Store.Insert(id, "vectors.pdf", 50, id.ToString(), [chunk], [[1, 0, 0]], "model:3", default);
        var matches = await fixture.Store.Search("unmatchedterm", [id], [1, 0, 0], "model:3", default);
        Assert.Equal(id, Assert.Single(matches).DocumentId);
        Assert.Equal(2, matches[0].Page);
        Assert.Empty(await fixture.Store.Search("unmatchedterm", [id], [1, 0, 0], "other:3", default));
        Assert.Empty(await fixture.Store.Search("unmatchedterm", [id], [1, 0], "model:3", default));
        Assert.True(await fixture.Store.Reindex(id, [chunk], [[0, 1, 0]], "new:3", default));
        Assert.Empty(await fixture.Store.Search("unmatchedterm", [id], [1, 0, 0], "model:3", default));
        Assert.Single(await fixture.Store.Search("unmatchedterm", [id], [0, 1, 0], "new:3", default));
        await fixture.Store.Delete(id, default);
        Assert.Empty(await fixture.Store.Search("unmatchedterm", [id], [0, 1, 0], "new:3", default));
    }

    [IntegrationFact]
    public async Task LocalAiPathReturnsCitedAnswerAndRejectsInventedCitations()
    {
        var invalidCitation = false;
        using var http = new HttpClient(new ProviderHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/embed")
            {
                using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var count = body.RootElement.GetProperty("input").GetArrayLength();
                return System.Text.Json.JsonSerializer.Serialize(new { embeddings = Enumerable.Repeat(new[] { 1f, 0f, 0f }, count) });
            }
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                message = new { content = System.Text.Json.JsonSerializer.Serialize(new { answer = "The warranty lasts two years.", citations = new[] { invalidCitation ? 999 : 1 }, insufficientEvidence = false }) }
            });
        }))
        { BaseAddress = new Uri("http://localhost:11434") };
        var ai = new OllamaClient(http, Options.Create(new AiOptions { Enabled = true, Dimensions = 3 }));
        var workspace = new Workspace(fixture.Store, ai, NullLogger<Workspace>.Instance);
        var document = await workspace.Import("warranty.md", Encoding.UTF8.GetBytes("The copper warranty lasts two years."), default);
        Assert.Equal(1, document.Document.EmbeddedChunks);
        var answer = await workspace.Ask(new("Copper warranty?", [document.Document.Id]), default);
        Assert.Equal("generated", answer.Mode);
        Assert.Equal("The warranty lasts two years.", answer.Text);
        Assert.Single(answer.Sources);
        invalidCitation = true;
        var fallback = await workspace.Ask(new("Copper warranty?", [document.Document.Id]), default);
        Assert.Equal("extractive", fallback.Mode);
        Assert.Contains("without valid citations", fallback.Notice);
        await fixture.Store.Delete(document.Document.Id, default);
    }

    [IntegrationFact]
    public async Task ProviderOutageRetainsUploadAndReturnsExplicitKeywordFallback()
    {
        using var http = new HttpClient(new ProviderHandler(_ => throw new HttpRequestException("Provider offline")))
        { BaseAddress = new Uri("http://localhost:11434") };
        var ai = new OllamaClient(http, Options.Create(new AiOptions { Enabled = true, Dimensions = 3 }));
        var workspace = new Workspace(fixture.Store, ai, NullLogger<Workspace>.Instance);
        var document = await workspace.Import("outage.txt", Encoding.UTF8.GetBytes("The amber delivery takes five days."), default);
        Assert.Equal(0, document.Document.EmbeddedChunks);
        Assert.Contains("Keyword search", document.Notice);
        var answer = await workspace.Ask(new("Amber delivery?", [document.Document.Id]), default);
        Assert.Equal("extractive", answer.Mode);
        Assert.Contains("five days", answer.Text);
        Assert.Contains("could not generate", answer.Notice);
        var reindexError = await Assert.ThrowsAsync<InputException>(() => workspace.Reindex(document.Document.Id, default));
        Assert.Equal(503, reindexError.Status);
        Assert.Single(await fixture.Store.Chunks(document.Document.Id, default));
        await fixture.Store.Delete(document.Document.Id, default);
    }

    [IntegrationFact]
    public async Task RetrievalEvaluationMatchesSourcesAndAbstains()
    {
        using var seeded = await Client.PostAsync("/api/samples", null);
        seeded.EnsureSuccessStatusCode();
        var samples = (await seeded.Content.ReadFromJsonAsync<UploadResult[]>())!;
        var ids = samples.Select(s => s.Document.Id).ToArray();
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "evaluation.json"));
        using var cases = System.Text.Json.JsonDocument.Parse(json);
        foreach (var item in cases.RootElement.EnumerateArray())
        {
            var question = item.GetProperty("question").GetString()!;
            using var response = await Client.PostAsJsonAsync("/api/ask", new AskRequest(question, ids));
            response.EnsureSuccessStatusCode();
            var answer = (await response.Content.ReadFromJsonAsync<Answer>())!;
            if (item.TryGetProperty("abstain", out var abstain) && abstain.GetBoolean())
            {
                Assert.Equal("no-evidence", answer.Mode);
                Assert.Empty(answer.Sources);
            }
            else
            {
                Assert.Equal("extractive", answer.Mode);
                Assert.Contains(item.GetProperty("contains").GetString()!, answer.Text);
                Assert.Equal(item.GetProperty("document").GetString(), answer.Sources[0].DocumentName);
            }
        }
    }

    private sealed class ProviderHandler(Func<HttpRequestMessage, Task<string>> handle) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            new(HttpStatusCode.OK) { Content = new StringContent(await handle(request), Encoding.UTF8, "application/json") };
    }

    private async Task<HttpResponseMessage> Upload(string filename, string text)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(text)), "file", filename);
        return await Client.PostAsync("/api/documents", form);
    }
}

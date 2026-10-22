using System.Threading.RateLimiting;
using DocSense.Web;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = TextProcessing.MaxBytes + 64 * 1024);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = TextProcessing.MaxBytes + 32 * 1024);
builder.Services.AddProblemDetails();
builder.Services.AddOptions<AiOptions>().BindConfiguration("Ai")
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo), "Ai:BaseUrl must be an HTTP(S) service URL.")
    .Validate(o => o.Dimensions is > 0 and <= 4096, "Ai:Dimensions must be between 1 and 4096.")
    .ValidateOnStart();
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Database. See README.md for local setup.")));
builder.Services.AddSingleton<DocumentStore>();
builder.Services.AddHttpClient<OllamaClient>((sp, client) =>
{
    client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<AiOptions>>().Value.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(90);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddTransient<Workspace>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddConcurrencyLimiter("workspace", p => { p.PermitLimit = 4; p.QueueLimit = 0; p.QueueProcessingOrder = QueueProcessingOrder.OldestFirst; });
});

var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        // No CORS is enabled. A non-simple header prevents cross-origin form mutations.
        if (context.Request.Method is "POST" or "DELETE" && context.Request.Headers["X-DocSense"] != "1")
        {
            await Results.Problem(statusCode: 403, detail: "Use the DocSense workspace or send the X-DocSense: 1 header.").ExecuteAsync(context);
            return;
        }
    }
    try { await next(); }
    catch (InputException e) { await Results.Problem(statusCode: e.Status, detail: e.Message).ExecuteAsync(context); }
    catch (BadHttpRequestException e) { await Results.Problem(statusCode: e.StatusCode, detail: "The request is invalid or too large.").ExecuteAsync(context); }
    catch (InvalidDataException) { await Results.Problem(statusCode: 400, detail: "The upload could not be read. Choose a file up to 8 MB.").ExecuteAsync(context); }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.MapGet("/health", async (DocumentStore store, CancellationToken ct) =>
    await store.Healthy(ct) ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));
var api = app.MapGroup("/api").RequireRateLimiting("workspace");
api.MapGet("/status", (IOptions<AiOptions> ai) => new
{
    aiEnabled = ai.Value.Enabled,
    mode = ai.Value.Enabled ? "Ollama configured" : "Extractive mode",
    maxUploadBytes = TextProcessing.MaxBytes,
    workspace = "local"
});
api.MapGet("/documents", (DocumentStore store, CancellationToken ct) => store.List(ct));
api.MapPost("/documents", async (HttpRequest request, Workspace workspace, CancellationToken ct) =>
{
    if (!request.HasFormContentType) throw new InputException("Send a multipart form with one file.", 415);
    var form = await request.ReadFormAsync(ct);
    if (form.Files.Count != 1) throw new InputException("Upload one document at a time.");
    var file = form.Files[0];
    if (file.Length is 0 or > TextProcessing.MaxBytes) throw new InputException("Choose a non-empty file up to 8 MB.", 413);
    await using var stream = file.OpenReadStream();
    using var buffer = new MemoryStream();
    await stream.CopyToAsync(buffer, ct);
    var result = await workspace.Import(file.FileName, buffer.ToArray(), ct);
    return Results.Json(result, statusCode: result.Duplicate ? 200 : 201);
});
api.MapGet("/documents/{id:guid}/chunks", async (Guid id, DocumentStore store, CancellationToken ct) =>
{
    var chunks = await store.Chunks(id, ct);
    return chunks.Count == 0 ? Results.NotFound() : Results.Ok(chunks);
});
api.MapDelete("/documents/{id:guid}", async (Guid id, DocumentStore store, CancellationToken ct) =>
    await store.Delete(id, ct) ? Results.NoContent() : Results.NotFound());
api.MapPost("/documents/{id:guid}/reindex", async (Guid id, Workspace workspace, CancellationToken ct) =>
{
    await workspace.Reindex(id, ct);
    return Results.NoContent();
});
api.MapPost("/samples", async (Workspace workspace, CancellationToken ct) =>
{
    List<UploadResult> results = [];
    foreach (var sample in Workspace.Samples)
        results.Add(await workspace.Import(sample.Name, System.Text.Encoding.UTF8.GetBytes(sample.Text), ct));
    return Results.Ok(results);
});
api.MapPost("/ask", (AskRequest request, Workspace workspace, CancellationToken ct) => workspace.Ask(request, ct));
await app.Services.GetRequiredService<DocumentStore>().Initialize(CancellationToken.None);
app.Run();
public partial class Program;

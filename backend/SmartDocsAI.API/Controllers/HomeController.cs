using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;

namespace SmartDocsAI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HomeController : ControllerBase
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);

    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public HomeController(
        AppDbContext context,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new
    {
        Service = "SmartDocs AI API",
        Status = "ok",
        Timestamp = DateTimeOffset.UtcNow
    });

    [HttpGet("ready")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(HealthTimeout);

        var databaseReady = await CheckDatabaseAsync(timeoutSource.Token);
        var qdrantReady = await CheckHttpServiceAsync(
            _configuration["QdrantSettings:BaseUrl"] ?? "http://localhost:6333",
            "/readyz",
            timeoutSource.Token);
        var ollamaReady = await CheckHttpServiceAsync(
            _configuration["OllamaSettings:BaseUrl"] ?? "http://localhost:11434",
            "/api/tags",
            timeoutSource.Token);
        var ready = databaseReady && qdrantReady && ollamaReady;

        var payload = new
        {
            Service = "SmartDocs AI API",
            Status = ready ? "hazır" : "hazır değil",
            Dependencies = new
            {
                PostgreSql = databaseReady ? "hazır" : "erişilemiyor",
                Qdrant = qdrantReady ? "hazır" : "erişilemiyor",
                Ollama = ollamaReady ? "hazır" : "erişilemiyor"
            },
            Timestamp = DateTimeOffset.UtcNow
        };

        return ready
            ? Ok(payload)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }

    private async Task<bool> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _context.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is DbException or InvalidOperationException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> CheckHttpServiceAsync(
        string baseUrl,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(baseUrl);
            using var response = await client.GetAsync(path, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidOperationException or OperationCanceledException)
        {
            return false;
        }
    }
}

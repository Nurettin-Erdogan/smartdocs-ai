using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SmartDocsAI.API.Controllers;
using SmartDocsAI.API.Data;

namespace SmartDocsAI.API.Tests;

public sealed class HomeControllerTests
{
    [Fact]
    public async Task Ready_ReturnsOk_WhenAllDependenciesAreAvailable()
    {
        await using var fixture = await HealthFixture.CreateAsync(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK));

        var result = await fixture.Controller.Ready(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Ready_ReturnsServiceUnavailable_WhenOllamaIsUnavailable()
    {
        await using var fixture = await HealthFixture.CreateAsync(request =>
            new HttpResponseMessage(
                request.RequestUri?.AbsolutePath == "/api/tags"
                    ? System.Net.HttpStatusCode.ServiceUnavailable
                    : System.Net.HttpStatusCode.OK));

        var result = await fixture.Controller.Ready(CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusResult.StatusCode);
    }

    private sealed class HealthFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly AppDbContext _context;

        private HealthFixture(
            SqliteConnection connection,
            AppDbContext context,
            HomeController controller)
        {
            _connection = connection;
            _context = context;
            Controller = controller;
        }

        public HomeController Controller { get; }

        public static async Task<HealthFixture> CreateAsync(
            Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["QdrantSettings:BaseUrl"] = "http://qdrant.test",
                    ["OllamaSettings:BaseUrl"] = "http://ollama.test"
                })
                .Build();
            var factory = new FakeHttpClientFactory(handler);
            var controller = new HomeController(context, factory, configuration);
            return new HealthFixture(connection, context, controller);
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeHttpClientFactory(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new TestHttpMessageHandler(handler));
    }
}

using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthorization();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    foreach (var configuredProxy in builder.Configuration
                 .GetSection("ProxySettings:KnownProxies")
                 .Get<string[]>() ?? [])
    {
        if (!IPAddress.TryParse(configuredProxy, out var proxyAddress))
        {
            throw new InvalidOperationException(
                $"ProxySettings:KnownProxies contains an invalid IP address: {configuredProxy}");
        }

        options.KnownProxies.Add(proxyAddress);
    }
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfterSeconds = context.Lease.TryGetMetadata(
            MetadataName.RetryAfter,
            out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 60;

        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Çok fazla istek",
                Message = "İstek sınırına ulaştınız. Lütfen kısa bir süre sonra tekrar deneyin.",
                RetryAfterSeconds = retryAfterSeconds
            },
            cancellationToken);
    };

    options.AddPolicy("AuthPolicy", context => CreateFixedWindowPartition(
        $"auth:{GetClientKey(context)}",
        permitLimit: 10,
        window: TimeSpan.FromMinutes(1)));
    options.AddPolicy("ChatPolicy", context => CreateFixedWindowPartition(
        $"chat:{GetClientKey(context)}",
        permitLimit: 20,
        window: TimeSpan.FromMinutes(1)));
    options.AddPolicy("DocumentWritePolicy", context => CreateFixedWindowPartition(
        $"documents:{GetClientKey(context)}",
        permitLimit: 6,
        window: TimeSpan.FromMinutes(5)));
});

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5173", "http://127.0.0.1:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSingleton<DocumentProcessingGate>();
builder.Services.AddScoped<IDocumentProcessor, DocumentProcessor>();
builder.Services.AddScoped<IDocumentIndexingService, DocumentIndexingService>();
builder.Services.AddScoped<IDocumentDeletionService, DocumentDeletionService>();
builder.Services.AddHostedService<PendingDocumentIndexingWorker>();
builder.Services.AddHostedService<PendingDocumentDeletionWorker>();
builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
{
    var timeoutSeconds = builder.Configuration.GetValue<int?>("OllamaSettings:TimeoutSeconds") ?? 0;
    client.Timeout = timeoutSeconds <= 0
        ? Timeout.InfiniteTimeSpan
        : TimeSpan.FromSeconds(timeoutSeconds);
});
builder.Services.AddHttpClient<IQdrantService, QdrantService>(client =>
    client.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue<int?>("QdrantSettings:TimeoutSeconds") ?? 30));

var jwtTokenKey = builder.Configuration["JwtSettings:TokenKey"]
    ?? throw new InvalidOperationException("JwtSettings:TokenKey is missing.");
if (Encoding.UTF8.GetByteCount(jwtTokenKey) < 64)
{
    throw new InvalidOperationException(
        "JwtSettings:TokenKey must be at least 64 bytes for HMAC-SHA512.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtTokenKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

var app = builder.Build();

app.UseForwardedHeaders();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    await DatabaseSeeder.SeedAsync(db, configuration, app.Environment.IsDevelopment());

    try
    {
        var ollama = scope.ServiceProvider.GetRequiredService<IOllamaService>();
        var warmupTimeoutSeconds = Math.Clamp(
            configuration.GetValue<int?>("OllamaSettings:WarmupTimeoutSeconds") ?? 30,
            1,
            300);
        using var warmupTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            app.Lifetime.ApplicationStopping);
        warmupTimeout.CancelAfter(TimeSpan.FromSeconds(warmupTimeoutSeconds));
        await ollama.WarmupAsync(warmupTimeout.Token);
    }
    catch (Exception exception) when (
        !app.Lifetime.ApplicationStopping.IsCancellationRequested &&
        (exception is HttpRequestException or OperationCanceledException or InvalidOperationException))
    {
        app.Logger.LogWarning(exception, "Ollama sohbet modeli başlangıçta ısıtılamadı.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler();
}

var frontendPath = ResolveFrontendPath(app.Environment, app.Configuration);
var contentSecurityPolicy = app.Configuration["SecurityHeaders:ContentSecurityPolicy"]
    ?? "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; " +
       "form-action 'self'; img-src 'self' data:; object-src 'none'; " +
       "script-src 'self'; style-src 'self'; connect-src 'self'";
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=()";
        context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        context.Response.Headers["Content-Security-Policy"] = contentSecurityPolicy;
        return Task.CompletedTask;
    });

    await next();
});

if (frontendPath is not null)
{
    var fileProvider = new PhysicalFileProvider(frontendPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (app.Configuration.GetValue("Hosting:UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}

app.UseResponseCompression();
app.UseRouting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

if (frontendPath is not null)
{
    app.MapWhen(
        context =>
            !context.Request.Path.StartsWithSegments("/api") &&
            !Path.HasExtension(context.Request.Path),
        spaApp => spaApp.Run(async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(Path.Combine(frontendPath, "index.html"));
        }));
}

app.Run();

static RateLimitPartition<string> CreateFixedWindowPartition(
    string key,
    int permitLimit,
    TimeSpan window) =>
    RateLimitPartition.GetFixedWindowLimiter(
        key,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });

static string GetClientKey(HttpContext context) =>
    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
    ?? context.Connection.RemoteIpAddress?.ToString()
    ?? "unknown";

static string? ResolveFrontendPath(
    IWebHostEnvironment environment,
    IConfiguration configuration)
{
    var configuredPath = configuration["FrontendSettings:DistPath"];
    var candidates = new[]
    {
        configuredPath,
        Path.Combine(environment.ContentRootPath, "wwwroot"),
        Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "frontend", "dist"))
    };

    return candidates
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => Path.GetFullPath(path!))
        .FirstOrDefault(path =>
            Directory.Exists(path) && File.Exists(Path.Combine(path, "index.html")));
}

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// TokenService registration (Dependency Injection)
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IDocumentProcessor, DocumentProcessor>();
builder.Services.AddHttpClient<IOllamaService, OllamaService>();
builder.Services.AddHttpClient<IQdrantService, QdrantService>();

// JWT Authentication Configuration
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                builder.Configuration["JwtSettings:TokenKey"] ?? throw new InvalidOperationException("TokenKey is missing"))),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero // Token süresi dolduğunda anında geçersiz kılınsın
        };
    });

builder.Services.AddOpenApi();

var app = builder.Build();

var frontendPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "frontend"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    DatabaseSeeder.SeedAsync(db, configuration, app.Environment.IsDevelopment()).GetAwaiter().GetResult();
}


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = new PhysicalFileProvider(frontendPath)
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(frontendPath)
});
app.UseCors("FrontendDev");

// Kimlik doğrulama ve yetkilendirme middleware'lerini ekliyoruz
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapWhen(context =>
    !context.Request.Path.StartsWithSegments("/api") &&
    !Path.HasExtension(context.Request.Path), spaApp =>
{
    spaApp.Run(async context =>
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(Path.Combine(frontendPath, "index.html"));
    });
});

app.Run();
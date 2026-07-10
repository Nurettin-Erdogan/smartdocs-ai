using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Helpers;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Services
{
    public static class DatabaseSeeder
    {
        public static async Task SeedAsync(AppDbContext context, IConfiguration configuration, bool isDevelopment)
        {
            await context.Database.MigrateAsync();

            if (!isDevelopment)
            {
                return;
            }

            if (await context.Users.AnyAsync())
            {
                return;
            }

            var adminName = configuration["SeedData:AdminFullName"] ?? "SmartDocs Admin";
            var adminEmail = configuration["SeedData:AdminEmail"] ?? "admin@smartdocs.ai";
            var adminPassword = configuration["SeedData:AdminPassword"] ?? "Admin123!";

            context.Users.Add(new User
            {
                FullName = adminName,
                Email = adminEmail.ToLowerInvariant(),
                PasswordHash = PasswordHasher.HashPassword(adminPassword),
                RoleId = 1,
                CreatedAt = DateTime.UtcNow
            });

            await context.SaveChangesAsync();
        }
    }
}

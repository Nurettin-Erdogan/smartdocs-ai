// Entity Framework ile PostgreSQL arasındaki bağlantıyı sağlayan merkez sınıftır.
// Veritabanındaki tablolar ve tablolar arasındaki ilişkiler burada tanımlanır.
// Tüm veritabanı işlemleri bu sınıf üzerinden gerçekleştirilir.

using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Role> Roles { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<Chunk> Chunks { get; set; }
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<Message> Messages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Rol ve Kullanıcı ilişkisi (Restrict: Rol silindiğinde kullanıcıların silinmesini engeller)
            modelBuilder.Entity<User>()
                .HasOne(u => u.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<Role>()
                .HasIndex(r => r.Name)
                .IsUnique();

            // Kullanıcı ve Belge ilişkisi (Cascade: Kullanıcı silinirse belgeleri de silinir)
            modelBuilder.Entity<Document>()
                .HasOne(d => d.User)
                .WithMany(u => u.Documents)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Belge ve Parça ilişkisi (Cascade: Belge silinirse parçaları da silinir)
            modelBuilder.Entity<Chunk>()
                .HasOne(c => c.Document)
                .WithMany(d => d.Chunks)
                .HasForeignKey(c => c.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Bir belgedeki parça sıra numarası tekil olmalıdır. Aynı PDF yeniden
            // işlense bile aynı sıradaki chunk'ın iki kez kaydedilmesini önler.
            modelBuilder.Entity<Chunk>()
                .HasIndex(c => new { c.DocumentId, c.ChunkIndex })
                .IsUnique();

            // Kullanıcının belgelerini yükleme tarihine göre listeleme sorgusunu hızlandırır.
            modelBuilder.Entity<Document>()
                .HasIndex(d => new { d.UserId, d.UploadDate });

            modelBuilder.Entity<Document>()
                .Property(d => d.IndexingStatus)
                .HasDefaultValue("Pending");

            // Kullanıcı ve Sohbet ilişkisi
            modelBuilder.Entity<Conversation>()
                .HasOne(c => c.User)
                .WithMany(u => u.Conversations)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Sohbet geçmişi kullanıcı ve oluşturulma tarihine göre okunur.
            modelBuilder.Entity<Conversation>()
                .HasIndex(c => new { c.UserId, c.CreatedAt });

            // Sohbet ve Mesaj ilişkisi
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Bir sohbetin mesajları zaman sırasıyla gösterilir.
            modelBuilder.Entity<Message>()
                .HasIndex(m => new { m.ConversationId, m.CreatedAt });

            // Varsayılan Rollerin Veritabanına Eklenmesi (Seed Data)
            modelBuilder.Entity<Role>().HasData(
                new Role { Id = 1, Name = "Admin" },
                new Role { Id = 2, Name = "Personel" },
                new Role { Id = 3, Name = "Misafir" }
            );
        }
    }
}

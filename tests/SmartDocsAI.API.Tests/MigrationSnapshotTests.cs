using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;

namespace SmartDocsAI.API.Tests;

public sealed class MigrationSnapshotTests
{
    // Migration'lar elle yazıldığı için anlık görüntünün modelle uyumu otomatik
    // garanti değildir. EF Core 9+ uyumsuzlukta MigrateAsync sırasında hata
    // fırlatır; bu test aynı sorunu uygulama başlamadan, bağlantı açmadan yakalar.
    [Fact]
    public void ModelSnapshot_MatchesTheCurrentModel()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=snapshot_check")
            .Options);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "Model değişti ama migration veya AppDbContextModelSnapshot güncellenmedi.");
    }
}

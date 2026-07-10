# SmartDocs AI veritabanı

Bu projede PostgreSQL, uygulamanın kalıcı verilerini tutar; vektör arama verileri ise Qdrant'ta tutulur. Veritabanı şemasının tek kaynağı Entity Framework Core migration'larıdır.

## Şema

| Tablo | Amaç |
| --- | --- |
| `Roles` | Admin, Personel ve Misafir rolleri |
| `Users` | Kullanıcı bilgileri ve parola hash'i |
| `Documents` | Yüklenen PDF dosyalarının metaverisi |
| `Chunks` | PDF'ten çıkarılan metin parçaları |
| `Conversations` | Kullanıcı sohbet oturumları |
| `Messages` | Soru-cevap geçmişi |

Temel ilişkiler: `Role 1-N User`, `User 1-N Document`, `Document 1-N Chunk`, `User 1-N Conversation`, `Conversation 1-N Message`.

## Önerilen geliştirme kurulumu

1. PostgreSQL servisinin çalıştığından emin olun.
2. PostgreSQL parolasını proje klasörüne yazmadan kullanıcı gizli ayarlarına ekleyin.
3. `backend` klasöründe uygulamayı çalıştırın: `dotnet run --project SmartDocsAI.API`.

Uygulama açılırken `DatabaseSeeder` otomatik olarak `Database.MigrateAsync()` çağırır. Böylece bekleyen migration'lar uygulanır, roller eklenir ve geliştirme ortamında örnek admin kullanıcısı oluşturulur.

## Manuel SQL kurulumu

PostgreSQL için veritabanını Entity Framework Core ile oluşturmak önerilir. `SmartDocsAI_Init.sql`, PostgreSQL için üretilmiş idempotent migration betiğidir; doğrudan `psql` veya pgAdmin Query Tool'da çalıştırılabilir. Betik, veritabanının önceden oluşturulmuş olmasını bekler.

- `dotnet ef database update` komutu `SmartDocsAI_Db` veritabanını ve tabloları oluşturur.
- `__EFMigrationsHistory` tablosu uygulanmış migration'ları kaydeder; bu sayede uygulama yeniden açıldığında EF aynı tabloları tekrar oluşturmaya çalışmaz.

## Şema değiştiğinde

Model değişikliğinden sonra `backend` klasöründe sırasıyla şunlar yapılır:

```powershell
dotnet ef migrations add AciklayiciMigrationAdi --project .\SmartDocsAI.API
dotnet ef database update --project .\SmartDocsAI.API
dotnet ef migrations script --idempotent --project .\SmartDocsAI.API --output ..\database\SmartDocsAI_Init.sql
```

Bu projede eklenen indeksler, kullanıcının belge ve sohbet geçmişini sıralarken performans sağlar. Ayrıca `(DocumentId, ChunkIndex)` benzersiz indeksi aynı PDF parçasının iki kez kaydedilmesini engeller.

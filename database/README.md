# SmartDocs AI veritabanı dosyaları

- `SmartDocsAI_Init.sql`: PostgreSQL için idempotent EF Core migration betiği
- `archive/sqlserver-*`: projenin eski SQL Server denemeleri; çalışan uygulama tarafından kullanılmaz

Güncel şemanın asıl kaynağı `backend/SmartDocsAI.API/Migrations` klasörüdür. Uygulama başlangıcında bekleyen migration'lar otomatik uygulanır.

Manuel güncelleme:

```powershell
dotnet ef database update --project backend\SmartDocsAI.API
```

İdempotent betiği yeniden üretme:

```powershell
dotnet ef migrations script --idempotent --project backend\SmartDocsAI.API --output database\SmartDocsAI_Init.sql
```

Tablolar, ilişkiler, indeksler ve yedekleme notları için `docs/database.md` belgesine bakın.

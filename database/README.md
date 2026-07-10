# Database

Bu klasör SmartDocs AI için SQL Server kurulum scriptini içerir.

## Dosyalar

- `SmartDocsAI_Init.sql`: Veritabanını, tabloları, foreign key'leri, index'leri ve başlangıç rollerini oluşturur.

## Kurulum

1. SQL Server Management Studio veya Azure Data Studio açın.
2. `SmartDocsAI_Init.sql` dosyasını çalıştırın.
3. Script `SmartDocsAI_Db` veritabanını oluşturur.
4. Uygulama tarafında `backend/SmartDocsAI.API/appsettings.json` içindeki connection string aynı veritabanını hedefler.

## Oluşan Yapı

- Roles
- Users
- Documents
- Chunks
- Conversations
- Messages

## Not

Bu proje EF Core migration da kullanıyor. Script hızlı kurulum içindir; migration ise kod tarafındaki model ile veritabanını senkron tutar.

# Veritabanı tasarımı

PostgreSQL kalıcı uygulama verisini, Qdrant ise yeniden üretilebilir vektör arama indeksini tutar. Şemanın asıl kaynağı `backend/SmartDocsAI.API/Migrations` altındaki EF Core migration'larıdır.

## İlişkiler

```text
Role 1 ── N User
User 1 ── N Document 1 ── N Chunk
User 1 ── N Conversation 1 ── N Message
```

Kullanıcı silinirse belge, chunk, sohbet ve mesajlar cascade ile silinir. Rol silme davranışı `Restrict` olarak tanımlıdır.

## Tablolar

### Roles

| Alan | Açıklama |
| --- | --- |
| `Id` | Birincil anahtar |
| `Name` | Tekil rol adı |

Seed edilen roller: `Admin`, `Personel`, `Misafir`.

### Users

| Alan | Açıklama |
| --- | --- |
| `Id` | Birincil anahtar |
| `FullName` | En fazla 100 karakter |
| `Email` | Normalize edilmiş, en fazla 150 karakter ve tekil |
| `PasswordHash` | BCrypt parola özeti; düz parola tutulmaz |
| `RoleId` | `Roles` dış anahtarı |
| `CreatedAt` | UTC oluşturulma zamanı |

### Documents

| Alan | Açıklama |
| --- | --- |
| `Id` | Birincil anahtar |
| `UserId` | Belge sahibinin dış anahtarı |
| `Title` | PDF dosya adından üretilen görünen başlık |
| `FileName` | Kullanıcıya gösterilen özgün dosya adı |
| `FileType` | `.pdf` |
| `FilePath` | Sunucudaki benzersiz fiziksel dosya yolu |
| `FileSize` | Bayt cinsinden boyut |
| `UploadDate` | UTC yüklenme zamanı |
| `IndexingStatus` | `Pending`, `Ready`, `Failed`, `NoContent` |
| `IndexingError` | En fazla 1000 karakterlik son indeksleme hata özeti |

`(UserId, UploadDate)` indeksi belge listesini hızlandırır.

### Chunks

| Alan | Açıklama |
| --- | --- |
| `Id` | Birincil anahtar |
| `DocumentId` | `Documents` dış anahtarı |
| `ChunkIndex` | Belge içindeki sıralı parça numarası |
| `Content` | PDF'den çıkarılan metin |
| `PageNumber` | Kaynak PDF sayfası |

`(DocumentId, ChunkIndex)` benzersiz indeksi aynı mantıksal parçanın iki kez kaydedilmesini engeller.

### Conversations

| Alan | Açıklama |
| --- | --- |
| `Id` | Birincil anahtar |
| `UserId` | Sohbet sahibinin dış anahtarı |
| `CreatedAt` | UTC oluşturulma zamanı |

`(UserId, CreatedAt)` indeksi son sohbetleri hızlandırır.

### Messages

| Alan | Açıklama |
| --- | --- |
| `Id` | Birincil anahtar |
| `ConversationId` | `Conversations` dış anahtarı |
| `Question` | Kullanıcı sorusu |
| `Answer` | Üretilen cevap |
| `CreatedAt` | UTC oluşturulma zamanı |

`(ConversationId, CreatedAt)` indeksi sohbet ayrıntısını kronolojik okur.

## Migration yönetimi

Uygulama başlangıcında `DatabaseSeeder`, `Database.MigrateAsync()` çağırır. Yeni model değişikliklerinde proje kökünden:

```powershell
dotnet ef migrations add AciklayiciMigrationAdi --project backend\SmartDocsAI.API
dotnet ef database update --project backend\SmartDocsAI.API
dotnet ef migrations script --idempotent --project backend\SmartDocsAI.API --output database\SmartDocsAI_Init.sql
```

Üretimde birden çok uygulama örneği aynı anda başlatılacaksa migration'ı dağıtım öncesi tek bir görev olarak çalıştırmak daha güvenlidir.

## Manuel kurulum betiği

`database/SmartDocsAI_Init.sql` idempotent PostgreSQL migration betiğidir. Veritabanı önceden oluşturulduktan sonra `psql` veya pgAdmin ile çalıştırılabilir. `__EFMigrationsHistory` uygulanmış migration'ları izler.

```powershell
psql -h localhost -U postgres -d SmartDocsAI_Db -f database\SmartDocsAI_Init.sql
```

## Yedekleme

Tam geri dönüş için birlikte yedeklenmesi gerekenler:

1. PostgreSQL veritabanı
2. `Uploads` volume/dizini
3. Qdrant storage veya snapshot

Qdrant verisi kaybolursa PDF dosyaları ve PostgreSQL chunk kayıtları üzerinden belgeler yeniden indekslenebilir. Bunun için uygulama düzeyinde toplu yeniden indeksleme işi eklemek üretim operasyonunu kolaylaştırır.

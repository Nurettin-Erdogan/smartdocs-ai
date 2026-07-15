# SmartDocs AI

SmartDocs AI, kullanıcının kendi PDF belgeleri üzerinde kaynak göstererek Türkçe soru-cevap yapmasını sağlayan, yerel çalışabilen bir RAG uygulamasıdır.

```text
PDF → metin çıkarma → parçalara ayırma → Ollama embedding
    → Qdrant anlamsal arama → Ollama cevap üretimi → kaynaklı cevap
```

## Öne çıkanlar

- React 19 + TypeScript ile duyarlı web arayüzü
- ASP.NET Core 10 API ve PostgreSQL kalıcı veri katmanı
- PdfPig ile PDF metni çıkarma ve örtüşmeli parçalara ayırma
- Ollama `/api/embed` ve `/api/generate` entegrasyonu
- Qdrant Query API ile kullanıcıya ait belgelerde filtreli arama
- Belge, sayfa, parça ve benzerlik skoru içeren kaynak gösterimi
- Tam sohbet geçmişi, yeni sohbet ve oturum süresi yönetimi
- Başarısız indekslemeyi tekrar deneme ve durum takibi
- Yeni vektörleri önce yazarak eski indeksi koruyan güvenli yeniden indeksleme
- JWT doğrulama, sahiplik kontrolleri ve endpoint bazlı hız sınırlama
- 20 MB sınırı, PDF imza kontrolü, güvenli fiziksel dosya adı ve işlem sınırları
- Docker Compose, çok aşamalı üretim imajı ve GitHub Actions CI
- Backend ve frontend birim testleri

## Mimari

| Katman | Teknoloji | Sorumluluk |
| --- | --- | --- |
| Arayüz | React, TypeScript, Vite | Kimlik doğrulama, belge ve sohbet deneyimi |
| API | ASP.NET Core 10 | Yetkilendirme, PDF akışı, RAG orkestrasyonu |
| İlişkisel veri | PostgreSQL, EF Core | Kullanıcı, belge, parça ve sohbet kayıtları |
| Vektör arama | Qdrant | Embedding saklama ve benzerlik araması |
| Yerel yapay zekâ | Ollama | Embedding ve Türkçe cevap üretimi |

Ayrıntılı akış için [sistem mimarisi](docs/system-architecture.md) belgesine bakın.

## Docker ile hızlı başlangıç

Gereksinimler: Docker Desktop ve Ollama. Ollama ana bilgisayarda çalışacaksa önce modelleri indirin:

```powershell
ollama pull nomic-embed-text
ollama pull llama3
```

Ardından proje kökünde:

```powershell
Copy-Item .env.example .env
```

`.env` içindeki şu değerleri mutlaka doldurun:

- `POSTGRES_PASSWORD`: güçlü bir PostgreSQL parolası
- `JWT_TOKEN_KEY`: en az 64 baytlık rastgele bir anahtar

Windows PowerShell ile uygun JWT anahtarı üretme örneği:

```powershell
$bytes = New-Object byte[] 64
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

Servisleri başlatın:

```powershell
docker compose up --build
```

Uygulama varsayılan olarak `http://localhost:8080` adresinde açılır. İlk kullanıcıyı arayüzdeki **Kayıt Ol** sekmesinden oluşturabilirsiniz.

Ana bilgisayardaki Ollama konteynerden erişilemiyorsa Ollama'yı `OLLAMA_HOST=0.0.0.0:11434` ile yeniden başlatın veya [Docker içi Ollama seçeneğini](docs/deployment.md#docker-içinde-ollama) kullanın.

## Yerel geliştirme

Gereksinimler:

- .NET 10 SDK
- Node.js 24 (Node 20 de mevcut frontend için uygundur)
- PostgreSQL 17+
- Qdrant 1.18+
- Ollama ve `nomic-embed-text`, `llama3` modelleri

Backend gizli ayarlarını kaynak koda yazmadan tanımlayın:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=SmartDocsAI_Db;Username=postgres;Password=PAROLA" --project backend\SmartDocsAI.API
dotnet user-secrets set "JwtSettings:TokenKey" "EN_AZ_64_BAYT_RASTGELE_ANAHTAR" --project backend\SmartDocsAI.API
dotnet user-secrets set "SeedData:AdminPassword" "GELISTIRME_ADMIN_PAROLASI" --project backend\SmartDocsAI.API
```

Backend:

```powershell
dotnet run --project backend\SmartDocsAI.API
```

Development ortamında migration'lar otomatik uygulanır. Veritabanında kullanıcı yoksa `SeedData:AdminPassword` ile `admin@smartdocs.ai` hesabı oluşturulur.

Frontend:

```powershell
Set-Location frontend
npm ci
npm run dev
```

Vite `http://localhost:5173` adresinde çalışır ve `/api` isteklerini `http://localhost:5129` adresine yönlendirir.

## Test ve doğrulama

```powershell
dotnet test tests\SmartDocsAI.API.Tests\SmartDocsAI.API.Tests.csproj --configuration Release

Set-Location frontend
npm ci
npm run typecheck
npm test
npm run build
```

CI aynı kontrolleri çalıştırır ve son aşamada üretim Docker imajını derler.

## Temel API

| Metot | Yol | Açıklama |
| --- | --- | --- |
| `POST` | `/api/auth/register` | Hesap oluşturur ve JWT döndürür |
| `POST` | `/api/auth/login` | Oturum açar ve JWT döndürür |
| `GET` | `/api/documents` | Kullanıcının belgelerini listeler |
| `POST` | `/api/documents/upload` | PDF yükler ve indeksler |
| `DELETE` | `/api/documents/{id}` | Belgeyi ve vektörlerini siler |
| `POST` | `/api/documents/{id}/reindex` | Belgeyi yeniden indeksler |
| `POST` | `/api/chat` | Kaynaklı cevap üretir |
| `GET` | `/api/chat/history` | Son 50 sohbetin özetini getirir |
| `GET` | `/api/chat/{conversationId}` | Bir sohbetin tüm mesajlarını getirir |
| `GET` | `/api/home` | Basit canlılık yanıtı verir |

Korumalı endpointlerde `Authorization: Bearer <token>` başlığı gerekir. İstek/yanıt örnekleri ve durum kodları [API belgesinde](docs/api.md) bulunur.

## Belge durumları

- `Pending`: indeksleme sürüyor
- `Ready`: sohbet aramasına hazır
- `Failed`: dış servis veya indeksleme hatası oluştu; tekrar denenebilir
- `NoContent`: PDF'den kullanılabilir metin çıkarılamadı
- `Deleting`: Qdrant, dosya ve veritabanı temizliği tamamlanana kadar kalıcı silme kuyruğunda

Sohbet yalnızca `Ready` belgeleri arar. Her kullanıcı sadece kendi belge ve sohbetlerine erişebilir.

## Yapılandırma

| Anahtar | Varsayılan | Açıklama |
| --- | --- | --- |
| `JwtSettings:LifetimeMinutes` | `480` | JWT ömrü |
| `OllamaSettings:TimeoutSeconds` | `0` | Ollama HTTP zaman aşımı; `0` sınırsız bekler |
| `OllamaSettings:KeepAlive` | `-1` | Sohbet modelini hızlı yanıt için bellekte tutma süresi; `-1` sürekli tutar |
| `QdrantSettings:VectorSize` | `768` | Embedding boyutu; modelle aynı olmalı |
| `QdrantSettings:UpsertBatchSize` | `64` | Vektör yazma paket boyutu |
| `RagSettings:SearchLimit` | `4` | Cevap bağlamına alınan parça sayısı |
| `RagSettings:MinimumScore` | `0.35` | En düşük benzerlik skoru |
| `DocumentProcessingSettings:MaxChunks` | `2000` | Tek PDF için güvenli parça sınırı |
| `DocumentProcessingSettings:MaxPages` | `500` | Tek PDF için sayfa sınırı |
| `DocumentProcessingSettings:MaxExtractedCharacters` | `2000000` | Açılmış PDF metni için karakter bütçesi |
| `DocumentProcessingSettings:TimeoutSeconds` | `60` | PDF ayrıştırma zaman bütçesi |
| `DocumentProcessingSettings:MaxConcurrentDocuments` | `2` | Aynı anda ayrıştırılabilecek PDF sayısı |
| `DocumentDeletionSettings:RetryIntervalSeconds` | `30` | Bekleyen kalıcı silmeleri yeniden deneme aralığı |
| `ProxySettings:KnownProxies` | `[]` | `X-Forwarded-*` başlıklarına güvenilecek proxy IP'leri |
| `Hosting:UseHttpsRedirection` | `true` | Doğrudan barındırmada HTTPS yönlendirmesi |

ASP.NET ortam değişkenlerinde `:` yerine `__` kullanılır; örneğin `RagSettings__MinimumScore`.

## Proje yapısı

```text
backend/SmartDocsAI.API/       ASP.NET Core API
frontend/                      React arayüzü ve Vitest testleri
tests/SmartDocsAI.API.Tests/   xUnit servis testleri
database/                      PostgreSQL migration betiği ve arşiv
docs/                          Mimari, API, veritabanı ve dağıtım belgeleri
.github/workflows/ci.yml       CI iş akışı
Dockerfile                     Üretim imajı
docker-compose.yml             Uygulama servisleri
```

## Üretim notları

- `.env`, JWT anahtarı ve parolaları Git'e eklemeyin.
- Uygulamayı TLS sonlandıran bir reverse proxy arkasında çalıştırın.
- PostgreSQL, Qdrant ve `Uploads` volume'larını düzenli yedekleyin.
- Qdrant koleksiyonunun vektör boyutunu embedding modeli değiştiğinde birlikte güncelleyin ve belgeleri yeniden indeksleyin.
- Ölçekli kurulumda tek süreç içi yeniden indeksleme kilidi yerine dağıtık iş kuyruğu kullanın.

Dağıtım ayrıntıları için [deployment.md](docs/deployment.md), şema ayrıntıları için [database.md](docs/database.md) dosyasına bakın.

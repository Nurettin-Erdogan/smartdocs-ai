# SmartDocs AI

SmartDocs AI, kullanıcıların PDF belgelerini yükleyip yalnızca kendi belgelerine
dayalı yapay zekâ cevapları alabildiği RAG tabanlı bir web uygulamasıdır.

## Mevcut özellikler

- JWT ile kayıt ve giriş
- Kullanıcıya özel PDF yükleme, listeleme ve silme
- PDF imzası, dosya boyutu ve güvenli dosya adı kontrolleri
- PdfPig ile metin çıkarma ve örtüşmeli chunk oluşturma
- Ollama ile embedding ve Türkçe cevap üretimi
- Qdrant ile kullanıcı belgelerine filtrelenmiş anlamsal arama
- Kaynak belge başlığı, sayfa, parça ve benzerlik skoru
- Sohbet geçmişi ve son mesajlarla konuşma bağlamı
- PostgreSQL ve Entity Framework Core migration'ları
- Giriş/kayıt endpointleri için IP bazlı hız sınırı

## Teknolojiler

| Katman | Teknoloji |
| --- | --- |
| Frontend | React 19, TypeScript, Vite, Fetch API |
| Backend | ASP.NET Core 10 Web API |
| Veritabanı | PostgreSQL, Entity Framework Core |
| Vektör veritabanı | Qdrant |
| Yapay zekâ | Ollama (`nomic-embed-text`, `llama3`) |
| PDF işleme | PdfPig |

## Çalışma akışı

1. Kullanıcı PDF yükler.
2. Backend dosyayı doğrular ve güvenli bir adla saklar.
3. PDF metni sayfa bazında çıkarılır ve parçalara bölünür.
4. Document ve Chunk kayıtları PostgreSQL transaction'ında saklanır.
5. Chunk embeddingleri kontrollü gruplarla Ollama'da oluşturulur.
6. Vektörler belge kimliği ve sayfa bilgileriyle Qdrant'a yazılır.
7. Soru embeddinge çevrilir ve yalnızca kullanıcının belgelerinde aranır.
8. İlgili parçalar ve son sohbet mesajları Ollama prompt'una eklenir.
9. Cevap ve kaynaklar kullanıcıya gösterilir, sohbet geçmişine kaydedilir.

## Gereksinimler

- .NET 10 SDK
- Node.js ve npm
- PostgreSQL
- Qdrant
- Ollama
- Ollama modelleri: `nomic-embed-text` ve `llama3`

## Güvenli geliştirme ayarları

JWT anahtarı, PostgreSQL parolası ve geliştirme admin parolası Git'e
yazılmamalıdır. Backend projesi `UserSecretsId` içerir.

```powershell
dotnet user-secrets set "JwtSettings:TokenKey" "GUCLU_RASTGELE_ANAHTAR" --project backend\SmartDocsAI.API
dotnet user-secrets set "SeedData:AdminPassword" "GUCLU_ADMIN_PAROLASI" --project backend\SmartDocsAI.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=SmartDocsAI_Db;Username=postgres;Password=PAROLA" --project backend\SmartDocsAI.API
```

## Backend'i çalıştırma

PostgreSQL, Qdrant ve Ollama servislerini başlattıktan sonra:

```powershell
dotnet run --project backend\SmartDocsAI.API
```

Uygulama açılırken migration'lar otomatik uygulanır. Development ortamında
veritabanında hiç kullanıcı yoksa user-secrets içindeki parola ile
`admin@smartdocs.ai` hesabı oluşturulur.

## Frontend'i çalıştırma

```powershell
cd frontend
npm install
npm run dev
```

Vite `http://localhost:5173` adresinde açılır ve `/api` isteklerini varsayılan
olarak `http://localhost:5129` adresine yönlendirir.

Üretim çıktısı için:

```powershell
npm run build
```

Backend, `frontend/dist` mevcutsa arayüzü aynı uygulama üzerinden sunar.

## Temel API uçları

| Metot | Adres | Açıklama |
| --- | --- | --- |
| POST | `/api/auth/register` | Kullanıcı oluşturur |
| POST | `/api/auth/login` | JWT üretir |
| GET | `/api/documents` | Kullanıcının belgelerini listeler |
| POST | `/api/documents/upload` | PDF yükler ve indeksler |
| DELETE | `/api/documents/{id}` | Dosya, PostgreSQL ve Qdrant verilerini siler |
| POST | `/api/chat` | Belgelere dayalı cevap üretir |
| GET | `/api/chat/history` | Sohbet geçmişini getirir |
| GET | `/api/chat/{conversationId}` | Tek sohbeti getirir |

## Proje yapısı

```text
smartdocs-ai/
├── backend/SmartDocsAI.API/   ASP.NET Core API
├── frontend/                  React + TypeScript arayüz
├── database/                  PostgreSQL migration SQL'i ve arşiv
├── docs/                      Analiz ve tasarım belgeleri
└── notes/                     Proje notları
```

## Durum

Proje aktif geliştirme aşamasındadır. Temel MVP akışı uygulanmıştır; otomatik
testler, Docker Compose ve üretim gözlemlenebilirliği sonraki ana çalışmalardır.

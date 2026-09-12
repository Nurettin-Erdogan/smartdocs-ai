# SmartDocs AI

<p align="center">
  <img src="frontend/public/social-preview.png" alt="SmartDocs AI — kaynaklı PDF yanıtları" width="1100">
</p>

<p align="center">
  <a href="https://github.com/Nurettin-Erdogan/smartdocs-ai/actions/workflows/ci.yml"><img src="https://github.com/Nurettin-Erdogan/smartdocs-ai/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI durumu"></a>
  <a href="https://github.com/Nurettin-Erdogan/smartdocs-ai/actions/workflows/codeql.yml"><img src="https://github.com/Nurettin-Erdogan/smartdocs-ai/actions/workflows/codeql.yml/badge.svg?branch=main" alt="CodeQL güvenlik analizi"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-0f766e.svg" alt="MIT License"></a>
  <a href="https://smartdocs-ai-henna.vercel.app"><img src="https://img.shields.io/badge/live-Vercel-0f766e.svg" alt="Canlı demo"></a>
  <img src="https://img.shields.io/badge/.NET-10-0f766e.svg" alt=".NET 10">
</p>

**PDF'e soru sor, cevabın hangi metne dayandığını gör.**

SmartDocs AI; PDF belgelerinden kaynaklı cevap üreten, cevabın dayandığı sayfa ve metin parçalarını incelemeyi sağlayan bir **RAG tabanlı belge asistanıdır**. Projede hem tarayıcıdan hemen denenebilen Gemini destekli bir vitrin sürümü hem de Docker ile çalışan tam yerel **ASP.NET Core + PostgreSQL + Qdrant + Ollama** kurulumu bulunur.

<p align="center">
  <a href="https://smartdocs-ai-henna.vercel.app"><strong>Canlı demoyu aç →</strong></a>
  &nbsp;·&nbsp;
  <a href="#docker-ile-hızlı-başlangıç"><strong>Yerelde çalıştır →</strong></a>
  &nbsp;·&nbsp;
  <a href="docs/system-architecture.md"><strong>Mimari</strong></a>
  &nbsp;·&nbsp;
  <a href="docs/demo-guide.md"><strong>Demo rehberi</strong></a>
</p>

## Kısa özet

| | |
| --- | --- |
| **Problem** | LLM cevabının belge içinde hangi bilgiye dayandığını görmek zor olabilir. |
| **Çözüm** | Kaynak parçası, sayfa referansı ve iddia–alıntı kontrolüyle incelenebilir cevap akışı. |
| **Backend** | ASP.NET Core 10, EF Core, PostgreSQL |
| **RAG** | PdfPig, Ollama embeddings, Qdrant semantic search, Ollama generation |
| **Frontend** | React 19, TypeScript, Vite |
| **Güvenlik** | JWT, sahiplik kontrolleri, rate limiting, PDF doğrulama, CodeQL |
| **DevOps** | Docker Compose, GitHub Actions CI, çok aşamalı production image |
| **Canlı demo** | Vercel + Google Gemini; kurulum gerektirmeden denenebilir |

<p align="center">
  <img src="docs/screenshots/sourced-answer-demo.jpg" alt="SmartDocs AI kaynaklı cevap ve sayfa referansları" width="1100">
</p>

## Neler gösteriyor?

- PDF metnini çıkarma, parçalara ayırma ve indeksleme
- Ollama ile embedding üretimi
- Qdrant üzerinde kullanıcıya ait belgelerde filtreli semantic search
- RAG bağlamından kaynaklı cevap üretimi
- Belge, sayfa, parça ve benzerlik skoru içeren kaynak gösterimi
- İddia bazlı alıntı eşleştirme ve yetersiz kanıt akışı
- İlgili PDF sayfasını açan ve metni vurgulayan kaynak önizleme
- JWT authentication ve kullanıcı bazlı belge/sohbet sahipliği
- Başarısız indeksleme için retry ve güvenli yeniden indeksleme
- Backend + frontend testleri, CI ve CodeQL taraması

## Çalışma akışı

```text
Tam yerel kurulum

PDF
 └─> PdfPig ile metin çıkarma
      └─> chunking
           └─> Ollama embedding
                └─> Qdrant vector search
                     └─> Ollama cevap üretimi
                          └─> kaynak + sayfa + kanıt kontrolü
```

```text
Canlı vitrin

PDF
 └─> tarayıcıda metin çıkarma
      └─> ilgili metin parçalarını seçme
           └─> Vercel Function
                └─> Google Gemini
                     └─> kaynaklı cevap
```

## Canlı sürüm ve tam yerel kurulum

| | Canlı vitrin | Yerel kurulum |
| --- | --- | --- |
| Amaç | Hızlı ürün demosu | Tam RAG mimarisini çalıştırmak |
| PDF işleme | Tarayıcı | ASP.NET Core + PdfPig |
| Model | Google Gemini | Ollama |
| Arama | Tarayıcıdaki metin seçimi | Ollama embeddings + Qdrant |
| Kalıcı veri | Yok | PostgreSQL + Qdrant + dosya hacimleri |
| Kullanıcı hesabı | Vitrin oturumu | JWT tabanlı gerçek kullanıcı hesabı |
| Veri akışı | Seçilen metin AI isteğinde Gemini'ye gider | Varsayılan kurulumda model servisleri yereldir |

> **Gizlilik notu:** Canlı demo tamamen yerel değildir. AI çağrısında soru, seçilen belge metinleri ve yakın sohbet geçmişi Vercel üzerinden Gemini'ye gönderilir. Gizli belgeler için tam yerel kurulum tercih edilmelidir.

## Mühendislik kararları

### Kaynak gösterimi tek başına yeterli değil

Modelden cevapla birlikte iddia, kaynak numarası ve alıntı istenir. Sunucu, verilen alıntının gerçekten kaynak metninde bulunup bulunmadığını ve iddia ile belirli bir kelime örtüşmesi taşıyıp taşımadığını kontrol eder.

Bu mekanizma **doğruluk garantisi değildir**. Amaç, model çıktısını doğrudan kabul etmek yerine kullanıcıya inceleyebileceği bir kanıt izi vermektir.

### Güvenli yeniden indeksleme

Bir belge yeniden indekslenirken yeni vektörler hazırlanıp başarıyla yazılmadan önce eski indeks kaldırılmaz. Böylece başarısız bir yeniden indeksleme belgenin çalışan aramasını hemen bozmaz.

### Kullanıcı izolasyonu

Belge, sohbet ve Qdrant aramaları kullanıcı sahipliğiyle sınırlandırılır. Bir kullanıcı başka bir kullanıcının belge veya sohbet verisine erişemez.

### Hata durumunda kontrollü davranış

İndeksleme işlemleri durum bazlı takip edilir; geçici hatalar retry akışına alınabilir. Canlı vitrinde Gemini kullanılamadığında sınırlı yerel metin davranışı devreye girebilir ve bu cevap AI cevabı gibi sunulmaz.

## Mimari

| Katman | Teknoloji | Sorumluluk |
| --- | --- | --- |
| Frontend | React 19, TypeScript, Vite | Auth, belge yönetimi, sohbet ve kaynak görüntüleme |
| API | ASP.NET Core 10 | Yetkilendirme, PDF pipeline, RAG orkestrasyonu |
| Veri | PostgreSQL, EF Core | Kullanıcı, belge, chunk ve sohbet kayıtları |
| Vector DB | Qdrant | Embedding saklama ve semantic search |
| AI | Ollama | Embedding ve yerel cevap üretimi |
| PDF | PdfPig | Metin çıkarma ve sayfa eşleme |
| CI/Security | GitHub Actions, CodeQL | Test, build ve güvenlik analizi |

Daha ayrıntılı teknik akış: [docs/system-architecture.md](docs/system-architecture.md)

## Docker ile hızlı başlangıç

**Gereksinim:** Docker Desktop.

Windows'ta en kolay yol:

```powershell
.\start-smartdocs.ps1 -TarayiciyiAc
```

veya `start-smartdocs.cmd` dosyasını çalıştırın.

Manuel kurulum için:

```powershell
Copy-Item .env.example .env
```

`.env` içinde güçlü değerler belirleyin:

- `POSTGRES_PASSWORD`
- `JWT_TOKEN_KEY`

Ardından:

```powershell
docker compose up --build
docker compose exec ollama ollama pull nomic-embed-text
docker compose exec ollama ollama pull qwen2.5:3b
```

Uygulama varsayılan olarak:

```text
http://localhost:8080
```

adresinde açılır.

## Yerel geliştirme

Gereksinimler:

- .NET 10 SDK
- Node.js 20+
- PostgreSQL
- Qdrant
- Ollama

Backend:

```powershell
dotnet run --project backend\SmartDocsAI.API
```

Frontend:

```powershell
Set-Location frontend
npm ci
npm run dev
```

## Test ve kalite kapıları

Backend testleri:

```powershell
dotnet test tests\SmartDocsAI.API.Tests\SmartDocsAI.API.Tests.csproj --configuration Release
```

Frontend:

```powershell
Set-Location frontend
npm ci
npm run typecheck
npm test
npm run build
```

CI pipeline şu kontrolleri çalıştırır:

- backend testleri
- frontend typecheck
- frontend testleri
- production build
- Docker image build
- CodeQL ile C# ve JavaScript/TypeScript analizi

## Temel API

| Metot | Yol | Açıklama |
| --- | --- | --- |
| `POST` | `/api/auth/register` | Hesap oluşturur |
| `POST` | `/api/auth/login` | JWT oturumu açar |
| `GET` | `/api/documents` | Kullanıcının belgelerini listeler |
| `POST` | `/api/documents/upload` | PDF yükler ve işleme kuyruğuna alır |
| `POST` | `/api/documents/{id}/reindex` | Belgeyi yeniden indeksler |
| `DELETE` | `/api/documents/{id}` | Belgeyi ve ilgili vektörleri siler |
| `POST` | `/api/chat` | RAG tabanlı kaynaklı cevap üretir |
| `GET` | `/api/chat/history` | Sohbet geçmişini getirir |

Tam API dokümantasyonu: [docs/api.md](docs/api.md)

## Güvenlik ve dayanıklılık

- JWT authentication
- kullanıcı bazlı ownership kontrolleri
- endpoint rate limiting
- PDF imza ve boyut doğrulaması
- güvenli fiziksel dosya adları
- PDF sayfa / karakter / chunk işlem sınırları
- başarısız belge işlemleri için durum ve retry yönetimi
- gizli ayarları environment variables / user-secrets ile yönetme
- CodeQL güvenlik analizi

## Bilinen sınırlar

- Otomatik OCR yoktur; yalnızca taranmış görüntü içeren PDF'ler önce OCR işleminden geçirilmelidir.
- Çok sütunlu sayfalar ve tablolar PDF metin sırasını bozabilir.
- Kaynak eşleşmesi anlamsal doğruluk garantisi değildir.
- Canlı demo Gemini erişimine ve API kotasına bağlıdır.
- Canlı vitrin kalıcı belge arşivi değildir.
- Büyük ölçekli production kullanımında süreç içi iş yönetimi yerine dağıtık job queue tercih edilmelidir.

## Proje yapısı

```text
backend/SmartDocsAI.API/       ASP.NET Core API
frontend/                      React + TypeScript arayüzü
tests/SmartDocsAI.API.Tests/   xUnit backend testleri
docs/                          Mimari, API ve deployment belgeleri
database/                      Veritabanı yardımcı dosyaları
.github/workflows/             CI ve CodeQL
Dockerfile                     Production image
docker-compose.yml             Yerel servisler
```

## Dokümantasyon

- [Sistem mimarisi](docs/system-architecture.md)
- [API](docs/api.md)
- [Deployment](docs/deployment.md)
- [Veritabanı](docs/database.md)
- [Demo rehberi](docs/demo-guide.md)
- [Katkı rehberi](CONTRIBUTING.md)
- [Güvenlik politikası](SECURITY.md)

## English summary

**SmartDocs AI** is a source-aware PDF assistant with a full local RAG stack. The local architecture combines ASP.NET Core, React, PostgreSQL, Qdrant and Ollama; a separate Vercel showcase provides a zero-install Gemini-powered demo. Answers include source references and heuristic claim–quote checks designed to make model output easier to inspect, not to guarantee factual correctness.

## Lisans

MIT — ayrıntılar için [LICENSE](LICENSE).
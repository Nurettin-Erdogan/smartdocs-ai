# SmartDocs AI

SmartDocs AI, kullanıcıların PDF belgelerini yükleyip yalnızca kendi belgelerine dayalı yapay zeka cevapları alabildiği RAG tabanlı bir web uygulamasıdır.

Kısa anlatım:

```text
Kullanıcı PDF yükler
-> Backend PDF'i doğrular, metni çıkarır ve parçalara böler
-> PostgreSQL belge, kullanıcı, sohbet ve parça kayıtlarını tutar
-> Qdrant PDF parçalarının vektör indeksini tutar
-> Ollama embedding ve Türkçe cevap üretir
-> Frontend cevapları ve kaynak parçaları kullanıcıya gösterir
```

## Projenin Amacı

Bu projenin amacı, kullanıcıların uzun PDF dokümanları içinde manuel arama yapmak yerine doğal dille soru sorabilmesini sağlamaktır. Sistem cevabı genel internet bilgisinden değil, kullanıcının yüklediği PDF içeriklerinden üretmeye çalışır.

Örnek kullanım:

```text
Kullanıcı: "Bu PDF'de sınav başvuru şartları ne diyor?"
Sistem: İlgili PDF parçalarını bulur, cevabı üretir ve kaynak parçaları gösterir.
```

## Temel Özellikler

- JWT tabanlı kayıt ve giriş
- Kullanıcıya özel PDF yükleme, listeleme ve silme
- PDF uzantısı, PDF imzası, dosya boyutu ve güvenli dosya adı kontrolleri
- PdfPig ile PDF metni çıkarma
- PDF metnini parçalara ayırma
- PostgreSQL üzerinde kullanıcı, belge, parça, sohbet ve mesaj kayıtları
- Ollama ile embedding ve Türkçe cevap üretimi
- Qdrant ile anlamsal arama
- Sadece giriş yapan kullanıcının kendi belgelerinde arama
- Cevapla birlikte kaynak belge, sayfa, parça ve benzerlik skoru gösterimi
- Belge indeksleme durumu takibi: `Pending`, `Ready`, `Failed`, `NoContent`
- Başarısız indeksleme için tekrar indeksleme endpoint'i
- Frontend'de başarısız belgeler için tekrar indeksleme butonu
- Auth ve chat akışlarında daha kontrollü hata mesajları
- Hassas bilgilerin appsettings yerine user-secrets ile yönetilmesi

## Kullanılan Teknolojiler

| Katman | Teknoloji |
| --- | --- |
| Frontend | React 19, TypeScript, Vite |
| Backend | ASP.NET Core 10 Web API |
| ORM | Entity Framework Core |
| Veritabanı | PostgreSQL |
| Vektör veritabanı | Qdrant |
| Yapay zeka | Ollama |
| Embedding modeli | `nomic-embed-text` |
| Cevap modeli | `llama3` |
| PDF işleme | PdfPig |

## Sistem Mimarisi

```text
Frontend (React)
    |
    v
Backend API (ASP.NET Core)
    |
    +--> PostgreSQL
    |       - kullanıcılar
    |       - roller
    |       - belgeler
    |       - PDF parçaları
    |       - sohbetler
    |       - mesajlar
    |
    +--> Ollama
    |       - embedding üretimi
    |       - cevap üretimi
    |
    +--> Qdrant
            - PDF parçalarının vektör indeksleri
            - anlamsal benzerlik araması
```

## Çalışma Akışı

1. Kullanıcı sisteme kayıt olur veya giriş yapar.
2. Backend kullanıcıya JWT token üretir.
3. Kullanıcı PDF yükler.
4. Backend dosyanın gerçekten PDF olup olmadığını kontrol eder.
5. Dosya güvenli ve benzersiz bir adla `Uploads` klasörüne kaydedilir.
6. PDF metni PdfPig ile okunur.
7. Metin parçalara bölünür ve PostgreSQL'e kaydedilir.
8. Her parça için Ollama üzerinden embedding üretilir.
9. Embedding'ler Qdrant'a kaydedilir.
10. Belge başarıyla indekslenirse durumu `Ready` olur.
11. Kullanıcı soru sorduğunda soru embedding'e çevrilir.
12. Qdrant, soruya en yakın PDF parçalarını bulur.
13. Backend bu parçaları Ollama prompt'una ekler.
14. Ollama Türkçe cevap üretir.
15. Cevap ve kaynaklar frontend'de gösterilir.

## API Endpointleri

### Auth

| Metot | Endpoint | Açıklama |
| --- | --- | --- |
| POST | `/api/auth/register` | Yeni kullanıcı oluşturur |
| POST | `/api/auth/login` | Kullanıcı girişi yapar ve JWT token döner |

### Documents

| Metot | Endpoint | Açıklama |
| --- | --- | --- |
| GET | `/api/documents` | Giriş yapan kullanıcının belgelerini listeler |
| POST | `/api/documents/upload` | PDF yükler, parçalar ve indeksler |
| DELETE | `/api/documents/{id}` | Belgeyi, dosyayı ve Qdrant vektörlerini siler |
| POST | `/api/documents/{id}/reindex` | Belgenin mevcut parçalarını tekrar Qdrant'a indeksler |

### Chat

| Metot | Endpoint | Açıklama |
| --- | --- | --- |
| POST | `/api/chat` | PDF içeriklerine dayalı cevap üretir |
| GET | `/api/chat/history` | Kullanıcının sohbet geçmişini getirir |
| GET | `/api/chat/{conversationId}` | Tek bir sohbetin detayını getirir |

### Home

| Metot | Endpoint | Açıklama |
| --- | --- | --- |
| GET | `/api/home` | API'nin ayakta olup olmadığını kontrol etmek için basit endpoint |

## Güvenlik ve Sağlamlaştırma Çalışmaları

Bu projede sadece özellik eklenmedi, aynı zamanda güvenlik ve hata dayanıklılığı da iyileştirildi.

- JWT imza anahtarı koddan çıkarıldı ve user-secrets'a taşındı.
- Varsayılan admin parolası appsettings dosyasından kaldırıldı.
- PDF yüklemede dosya uzantısı ve dosya imzası kontrolü eklendi.
- Dosya adları doğrudan kullanılmak yerine güvenli hale getirildi.
- PDF yükleme işlemi transaction mantığıyla daha güvenli hale getirildi.
- Belge silinirken Qdrant tarafındaki vektörler de temizleniyor.
- Qdrant veya Ollama kapalıysa kullanıcıya daha anlaşılır hata mesajı dönülüyor.
- Belge indeksleme durumları takip ediliyor.
- Başarısız indeksleme durumunda tekrar indeksleme akışı eklendi.
- Runtime upload dosyaları Git'e alınmıyor.

## Gereksinimler

Projeyi tam çalıştırmak için aşağıdaki servisler gerekir:

- .NET 10 SDK
- Node.js ve npm
- PostgreSQL
- Ollama
- Qdrant
- Docker Desktop veya Qdrant'ın çalışacağı başka bir ortam

Ollama modelleri:

```powershell
ollama pull nomic-embed-text
ollama pull llama3
```

Qdrant genellikle Docker ile çalıştırılır:

```powershell
docker run -p 6333:6333 qdrant/qdrant
```

Not: Windows üzerinde Docker Desktop Linux container çalıştırmak için çoğu kurulumda WSL 2 ve Virtual Machine Platform özellikleri gerekir.

## Güvenli Geliştirme Ayarları

JWT anahtarı, admin parolası ve veritabanı parolası Git'e yazılmamalıdır. Backend projesinde user-secrets kullanılmalıdır.

```powershell
dotnet user-secrets set "JwtSettings:TokenKey" "GUCLU_RASTGELE_ANAHTAR" --project backend\SmartDocsAI.API
dotnet user-secrets set "SeedData:AdminPassword" "GUCLU_ADMIN_PAROLASI" --project backend\SmartDocsAI.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=SmartDocsAI_Db;Username=postgres;Password=PAROLA" --project backend\SmartDocsAI.API
```

## Backend'i Çalıştırma

Önce PostgreSQL, Ollama ve Qdrant servisleri çalışır durumda olmalıdır.

```powershell
dotnet run --project backend\SmartDocsAI.API
```

Development ortamında uygulama açılırken migration'lar otomatik uygulanır. Veritabanında hiç kullanıcı yoksa user-secrets içindeki admin parolası ile varsayılan admin hesabı oluşturulur.

Varsayılan admin e-postası:

```text
admin@smartdocs.ai
```

## Frontend'i Çalıştırma

```powershell
cd frontend
npm install
npm run dev
```

Vite varsayılan olarak şu adreste açılır:

```text
http://localhost:5173
```

Frontend `/api` isteklerini backend'e yönlendirir.

TypeScript kontrolü:

```powershell
npm run typecheck
```

Production build:

```powershell
npm run build
```

## Proje Yapısı

```text
smartdocs-ai/
├── backend/SmartDocsAI.API/   ASP.NET Core backend API
├── frontend/                  React + TypeScript arayüz
├── database/                  PostgreSQL kurulum SQL'i ve arşiv
└── README.md                  Proje anlatımı
```

## Mevcut Durum

Kod tarafında backend endpointleri hazırdır ve backend build başarılıdır. Frontend tarafında TypeScript kontrolü başarılıdır.

Canlı PDF sohbet akışının çalışması için Qdrant servisinin açık olması gerekir. Bu makinede Docker Desktop motoru başlamadığı için Qdrant container çalıştırılamamıştır. Bu nedenle canlı uçtan uca test için Docker/WSL altyapı izni gerekmektedir.

Kısa durum özeti:

```text
Backend endpointleri: hazır
Frontend arayüz: hazır
PostgreSQL bağlantısı: gerekli
Ollama: gerekli
Qdrant: gerekli
Docker/WSL: Qdrant local çalışacaksa gerekli
```

## Hocaya Kısa Anlatım

SmartDocs AI, PDF belgeleri üzerinden soru-cevap yapan bir RAG uygulamasıdır. Kullanıcı PDF yükler, backend PDF'i okur ve parçalara böler. Parçalar PostgreSQL'de saklanır, ayrıca embedding'e çevrilip Qdrant'a kaydedilir. Kullanıcı soru sorduğunda sistem Qdrant'ta ilgili PDF parçalarını bulur ve Ollama ile bu parçalara dayalı Türkçe cevap üretir.

Projede auth, doküman yönetimi, PDF yükleme, PDF silme, tekrar indeksleme ve chat endpointleri hazırdır. Ayrıca JWT anahtarı ve admin parolası gibi hassas bilgiler config dosyalarından çıkarılmış, PDF yükleme güvenliği artırılmış ve Qdrant/Ollama servis hataları daha kontrollü hale getirilmiştir.

Şu an kod tarafı hazırdır; canlı demo için eksik olan kısım local makinede Qdrant'ın çalışmasıdır. Qdrant Docker ile çalıştırıldığı için Docker Desktop'ın ve Windows tarafında gerekli WSL altyapısının açık olması gerekir.

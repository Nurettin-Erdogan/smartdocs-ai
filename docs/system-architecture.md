# Sistem mimarisi

SmartDocs AI, belge metnini ilişkisel veritabanında; arama vektörlerini Qdrant'ta tutan bir RAG uygulamasıdır. Cevap üretimi ve embedding işlemleri Ollama üzerinden yerel altyapıda çalışabilir.

```text
Tarayıcı / React
       │ HTTPS + JWT
       ▼
ASP.NET Core API
       ├── PostgreSQL ── kullanıcı, belge, chunk, sohbet, mesaj
       ├── Qdrant ────── embedding + belge/sayfa/chunk payload'ı
       ├── Ollama ────── /api/embed + /api/generate
       └── Uploads ───── benzersiz fiziksel PDF dosyaları
```

## Bileşen sınırları

### React frontend

- kayıt, giriş ve güvenli oturum temizleme
- PDF seçimi, istemci tarafı uzantı/boyut kontrolü
- belge durumlarını, sohbet geçmişini ve kaynakları gösterme
- `401` yanıtında merkezi oturum sonlandırma
- eski sohbetleri ayrıntı endpointinden ihtiyaç anında yükleme

### ASP.NET Core API

- JWT doğrulama ve kullanıcı sahipliği kontrolleri
- istek doğrulama ve endpoint bazlı hız sınırlama
- PDF'yi güvenli adla kaydetme ve PdfPig ile metin çıkarma
- PostgreSQL, Ollama ve Qdrant arasındaki indeksleme akışını yönetme
- sadece yeterli skorlu ve kullanıcıya ait sonuçlarla prompt oluşturma
- sohbet ve kaynak yanıtını üretme

### PostgreSQL

PostgreSQL uygulamanın kayıt kaynağıdır. Bir belgenin sohbet aramasına katılıp katılmayacağını `Documents.IndexingStatus` belirler. EF Core migration'ları şemanın asıl kaynağıdır ve uygulama başlangıcında uygulanır.

### Qdrant

Her nokta şu payload alanlarını taşır:

```text
documentId, chunkIndex, content, pageNumber, indexVersion
```

Arama, PostgreSQL'den alınan kullanıcının `Ready` belge kimlikleriyle filtrelenir. API ayrıca gelen sonuçlarda sahiplik filtresini yeniden uygular ve aynı belge/parça çiftinin eski indeks sürümlerini tekilleştirir.

### Ollama

- `POST /api/embed`: soru ve belge parçaları için embedding
- `POST /api/generate`: bulunan bağlamdan Türkçe cevap

Embedding modelinin boyutu Qdrant `VectorSize` ile aynı olmalıdır. Varsayılan `nomic-embed-text` yapılandırması 768 boyut kullanır.

## Belge yükleme akışı

1. JWT doğrulanır ve kullanıcı kimliği claim'den alınır.
2. Dosya uzantısı, boyutu ve `%PDF-` imzası doğrulanır.
3. PDF benzersiz fiziksel adla `Uploads` dizinine yazılır.
4. Belge kaydı `Pending` olarak oluşturulur.
5. API `202 Accepted` döner; tarayıcının yükleme bağlantısı artık işlemeyi tutmaz.
6. PostgreSQL tabanlı işçi belgeyi atomik olarak sahiplenip `Extracting` durumuna geçirir.
7. PdfPig sayfa metinlerini çıkarır; metin 800 karakterlik, 150 karakter örtüşmeli parçalara bölünür.
8. Belge `Indexing` durumuna geçer ve embedding'ler sınırlı paralel paketlerde üretilir.
9. Qdrant koleksiyonu yoksa oluşturulur ve vektörler paketler hâlinde yazılır.
10. Belge `Ready`, `Failed` veya `NoContent` durumuna geçirilir.

Geçici hata oluşursa belge gecikmeli olarak üç kez otomatik denenir. Süresi dolan işlem
kiraları başka bir işçi tarafından geri alınabilir. İndeksleme başarısız olsa bile PDF ve
çıkarılmış parçalar korunur; kullanıcı manuel yeniden indeksleme de başlatabilir.

## Güvenli yeniden indeksleme

```text
eski çalışan sürüm
       │
       ├── yeni embedding'leri üret
       ├── yeni indexVersion ile tüm noktaları yaz
       ├── diğer sürümleri temizlemeyi dene
       └── belgeyi Ready yap
```

Yeni sürüm tamamen yazılmadan eski sürüm silinmez. Eski sürüm temizliği geçici olarak başarısız olursa arama `(documentId, chunkIndex)` üzerinden en yüksek skorlu sonucu seçerek kopyaları bastırır.

## Soru-cevap akışı

1. Soru ve sohbet sahipliği doğrulanır.
2. Kullanıcının `Ready` belge kimlikleri PostgreSQL'den alınır.
3. Takip sorularında son konuşma bağlamı retrieval sorgusuna eklenir.
4. Soru Ollama ile embedding'e dönüştürülür.
5. Qdrant Query API, belge kimliği filtresi ve minimum skorla aranır.
6. En ilgili parçalar, son beş mesaj ve güvenlik talimatları prompt'a eklenir.
7. Ollama cevap üretir.
8. Soru/cevap PostgreSQL'e kaydedilir; kaynaklar istemciye döner.

Belge içindeki talimatlar veri kabul edilir; sistem prompt'u bunların uygulanmamasını açıkça söyler. Belgelerde dayanak yoksa modelden tahmin yürütmemesi istenir.

## Güvenlik sınırları

- kullanıcılar belge ve sohbetleri yalnızca kendi `UserId` değerleri üzerinden okuyabilir
- JWT HMAC-SHA512 anahtarı en az 64 bayt olmalıdır
- token ömrü varsayılan 8 saattir ve saat kayması toleransı sıfırdır
- upload, auth ve chat için ayrı sabit pencere hız limitleri vardır
- fiziksel dosya adı kullanıcı girdisinden bağımsız UUID'dir
- tek PDF 100 MB ve yapılandırılabilir chunk sayısıyla sınırlıdır
- dış servis timeout'ları ve iptal token'ları tüm akışa taşınır

## Tutarlılık ve bilinen ölçek sınırları

PostgreSQL ile Qdrant arasında dağıtık transaction yoktur. Kod kalıcı durum, işlem kirası
ve güvenli yazma sırasıyla riski azaltır; çok düğümlü büyük kurulumlarda outbox ve
periyodik artık vektör temizliği yine önerilir.

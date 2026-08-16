# SmartDocs AI API

Yerel geliştirme taban adresi `http://localhost:5129/api`, Docker Compose taban adresi `http://localhost:8080/api` şeklindedir. JSON alan adları camelCase biçimindedir.

## Kimlik doğrulama

`/auth` ve `/home` dışındaki endpointler JWT ister:

```http
Authorization: Bearer eyJhbGciOi...
```

JWT varsayılan olarak 8 saat geçerlidir. Süresi dolan veya geçersiz token `401 Unauthorized` üretir.

### Hesap oluşturma

`POST /api/auth/register`

```json
{
  "fullName": "Ada Lovelace",
  "email": "ada@example.com",
  "password": "guclu-parola"
}
```

Başarılı yanıt (`200`):

```json
{
  "id": 12,
  "fullName": "Ada Lovelace",
  "email": "ada@example.com",
  "role": "Personel",
  "token": "eyJhbGciOi..."
}
```

Olası yanıtlar: `400` doğrulama hatası, `409` e-posta kullanımda, `429` hız sınırı.

### Giriş

`POST /api/auth/login`

```json
{
  "email": "ada@example.com",
  "password": "guclu-parola"
}
```

Başarılı yanıt kayıt yanıtıyla aynıdır. Hatalı bilgiler `401` döndürür ve hesabın bulunup bulunmadığını açıklamaz.

## Belgeler

### Listeleme

`GET /api/documents`

```json
[
  {
    "id": 37,
    "title": "Kullanım Kılavuzu",
    "fileName": "kullanim-kilavuzu.pdf",
    "fileType": ".pdf",
    "fileSize": 248193,
    "uploadDate": "2026-07-14T10:30:00Z",
    "indexingStatus": "Ready"
  }
]
```

Yalnızca giriş yapan kullanıcının belgeleri döner.

### PDF yükleme

`POST /api/documents/upload`

İstek `multipart/form-data` olmalı ve dosya `file` alanında gönderilmelidir.

```bash
curl -X POST http://localhost:5129/api/documents/upload \
  -H "Authorization: Bearer TOKEN" \
  -F "file=@ornek.pdf"
```

Kurallar:

- yalnızca `.pdf`
- en fazla 100 MB
- içerik `%PDF-` imzasıyla başlamalı
- tek belgede en fazla yapılandırılmış `MaxChunks` değeri kadar parça

Başarılı yanıt `202 Accepted` ve `Pending` durumundaki belge nesnesidir. Metin çıkarma ve
indeksleme kalıcı arka plan kuyruğunda devam eder. Geçici dış servis hataları otomatik
olarak yeniden denenir; son deneme de başarısız olursa durum `Failed` olur.

### Silme

`DELETE /api/documents/{id}`

Belge kullanıcının değilse veya yoksa `404` döner. Qdrant'taki hazır indeks güvenli biçimde temizlenemiyorsa veri tutarlılığını korumak için `503` döner. Aktif yeniden indeksleme sırasında `409` döner.

### Yeniden indeksleme

`POST /api/documents/{id}/reindex`

Başarılı yanıt `202 Accepted` değeridir. Yeni embedding sürümü tamamen yazılmadan eski
çalışan sürüm silinmez. Yeniden indeksleme başarısız olursa daha önce `Ready` olan indeks
kullanılmaya devam eder.

Olası yanıtlar: `202`, `404`, `409` işlem sürüyor.

## Sohbet

### Soru sorma

`POST /api/chat`

Yeni sohbet:

```json
{
  "question": "Başvuru şartları nelerdir?"
}
```

Var olan sohbeti sürdürme:

```json
{
  "question": "İkinci şartı biraz açar mısın?",
  "conversationId": 9
}
```

Başarılı yanıt (`200`):

```json
{
  "conversationId": 9,
  "answer": "Belgeye göre başvuru için ...",
  "sources": [
    {
      "documentId": 37,
      "title": "Kullanım Kılavuzu",
      "chunkIndex": 4,
      "pageNumber": 3,
      "score": 0.8124,
      "content": "Başvuru için ..."
    }
  ]
}
```

Soru 1-2000 karakter olmalıdır. Arama yalnızca kullanıcının `Ready` belgelerinde yapılır. Yeterince benzer içerik yoksa `404`, hazır belge yoksa `400`, dış servis zaman aşımı/erişim sorunu varsa `503`, geçersiz dış servis yanıtı varsa `502` döner.

Arayüz aynı endpoint'e `Accept: application/x-ndjson` başlığıyla bağlanır. Bu durumda cevap tek parça JSON yerine `start`, `chunk`, `done` olaylarıyla üretildikçe aktarılır. Akış sırasında model hatası oluşursa `error` olayı gönderilir. Standart `application/json` istemcileri geriye uyumlu tek parça yanıt almaya devam eder.

### Sohbet özetleri

`GET /api/chat/history`

En yeni 50 sohbeti hafif özet biçiminde getirir:

```json
[
  {
    "conversationId": 9,
    "createdAt": "2026-07-14T10:42:00Z",
    "firstQuestion": "Başvuru şartları nelerdir?",
    "messageCount": 3
  }
]
```

### Sohbet ayrıntısı

`GET /api/chat/{conversationId}`

```json
{
  "conversationId": 9,
  "createdAt": "2026-07-14T10:42:00Z",
  "messages": [
    {
      "id": 21,
      "question": "Başvuru şartları nelerdir?",
      "answer": "Belgeye göre ...",
      "createdAt": "2026-07-14T10:42:02Z"
    }
  ]
}
```

Başka kullanıcıya ait sohbet için `404` döner.

## Canlılık

`GET /api/home`

```json
{
  "service": "SmartDocs AI API",
  "status": "ok",
  "timestamp": "2026-07-14T10:45:00+00:00"
}
```

Bu endpoint yalnızca API sürecinin yanıt verdiğini gösterir; PostgreSQL, Qdrant veya Ollama için derin sağlık kontrolü değildir.

`GET /api/home/ready` PostgreSQL, Qdrant ve Ollama bağlantılarını ayrı ayrı denetler. Tüm servisler hazırsa `200`, bağımlılıklardan biri kullanılamıyorsa `503` döner.

## Hata biçimleri

İş kuralı hataları genellikle şu biçimdedir:

```json
{
  "message": "Belge bulunamadı."
}
```

Model doğrulama ve beklenmeyen üretim hataları RFC Problem Details biçiminde dönebilir. İstemci her iki biçimi de destekler.

Hız sınırı yanıtı `429` kodu, `Retry-After` başlığı ve `retryAfterSeconds` alanı içerir.

## Hız sınırları

| Grup | Sınır |
| --- | --- |
| Kayıt ve giriş | IP başına dakikada 10 |
| Soru sorma | kullanıcı/IP başına dakikada 20 |
| Yükleme, silme, yeniden indeksleme | kullanıcı/IP başına 5 dakikada 6 |

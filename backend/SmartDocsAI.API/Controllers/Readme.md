# Controllers Klasörü

Controllerlar, frontend ile backend arasındaki iletişimi yönetir.

Frontend bir işlem yapmak istediğinde ilgili endpoint’e HTTP isteği gönderir. Controller bu isteği alır, gerekli kontrolleri yapar, veritabanını veya servisleri çağırır ve sonucu frontend’e döndürür.

## Genel Çalışma Akışı

```text
Frontend
   ↓ HTTP isteği
Controller
   ↓
Veritabanı / Servisler
   ↓
Controller
   ↓ HTTP cevabı
Frontend
```

Controller bütün işlemleri kendisi yapmaz.

Örneğin:

- Veritabanı işlemleri için `AppDbContext`
- PDF işleme için `DocumentProcessor`
- Embedding ve yapay zekâ cevabı için `OllamaService`
- Vektör araması için `QdrantService`
- JWT üretmek için `TokenService`

kullanılır.

---

## HomeController

Backend’in çalışıp çalışmadığını kontrol eder.

```http
GET /api/home
```

Cevap:

```text
SmartDocs AI Backend Çalışıyor!
```

---

## AuthController

Kullanıcı kayıt ve giriş işlemlerini yönetir.

```http
POST /api/auth/register
POST /api/auth/login
```

Görevleri:

- Yeni kullanıcı oluşturmak
- Aynı e-posta adresini kontrol etmek
- Parolayı hashleyerek kaydetmek
- Kullanıcı girişini doğrulamak
- JWT token oluşturmak
- Kullanıcı bilgilerini frontend’e göndermek

---

## DocumentsController

PDF işlemlerini yönetir.

```http
POST   /api/documents/upload
GET    /api/documents
DELETE /api/documents/{id}
POST   /api/documents/{id}/reindex
```

Görevleri:

- PDF yüklemek
- Dosyanın geçerli PDF olup olmadığını kontrol etmek
- PDF’yi sunucuya kaydetmek
- Belge bilgilerini PostgreSQL’e kaydetmek
- PDF metnini chunk’lara bölmek
- Ollama ile embedding oluşturmak
- Embedding’leri Qdrant’a kaydetmek
- Belgeleri listelemek ve silmek
- Başarısız indeksleme işlemini yeniden denemek

---

## ChatController

PDF belgelerine dayanarak soru-cevap işlemini yönetir.

```http
POST /api/chat
GET  /api/chat/history
GET  /api/chat/{conversationId}
```

Görevleri:

- Kullanıcının sorusunu almak
- Soruyu embedding’e dönüştürmek
- Qdrant’tan ilgili PDF parçalarını bulmak
- Önceki konuşmaları ve belge parçalarını prompta eklemek
- Ollama ile cevap üretmek
- Soru ve cevabı PostgreSQL’e kaydetmek
- Cevabı ve kaynakları frontend’e göndermek
- Sohbet geçmişini listelemek

---

## Kısa Özet

| Controller | Görevi |
|---|---|
| `HomeController` | Backend kontrolü |
| `AuthController` | Kayıt, giriş ve JWT |
| `DocumentsController` | PDF yükleme, işleme ve indeksleme |
| `ChatController` | RAG tabanlı soru-cevap ve sohbet geçmişi |
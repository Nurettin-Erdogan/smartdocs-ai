# SmartDocs AI API Design

---

# Authentication

## Register

POST /api/auth/register

Açıklama:
Yeni kullanıcı oluşturur.

---

## Login

POST /api/auth/login

Açıklama:
Kullanıcı giriş yapar.

---

# Documents

## Upload Document

POST /api/documents

Açıklama:
Yeni belge yükler.

---

## Get Documents

GET /api/documents

Açıklama:
Tüm belgeleri listeler.

---

## Delete Document

DELETE /api/documents/{id}

Açıklama:
Belgeyi siler.

---

## Get Document Detail

GET /api/documents/{id}

Açıklama:
Belge detayını getirir.

---

# Chat

## Ask AI

POST /api/chat

Açıklama:
Yapay zekaya soru gönderir.

---

## Chat History

GET /api/chat/history

Açıklama:
Geçmiş sohbetleri getirir.

---

# Dashboard

GET /api/dashboard

Açıklama:
Dashboard verilerini getirir.
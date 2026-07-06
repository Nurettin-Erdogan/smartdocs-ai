# SmartDocs AI System Architecture

## Amaç

SmartDocs AI, kullanıcıların yüklediği dokümanları analiz ederek yapay zekâ destekli soru-cevap yapılmasını sağlayan RAG (Retrieval-Augmented Generation) tabanlı bir doküman yönetim sistemidir.

---

# Genel Mimari

Kullanıcı
↓
React Frontend
↓
ASP.NET Core Web API
↓
SQL Server + Qdrant
↓
Ollama / OpenAI
↓
AI Cevabı

---

# Sistem Bileşenleri

## Frontend (React)

Görevleri

- Kullanıcı giriş ekranı
- Dashboard
- Doküman yönetimi
- Chat ekranı
- Raporlama

---

## Backend (ASP.NET Core Web API)

Görevleri

- API Servisleri
- JWT Authentication
- Doküman yönetimi
- PDF işleme
- AI ile iletişim
- SQL Server bağlantısı

---

## SQL Server

Görevleri

- Kullanıcı bilgileri
- Belgeler
- Sohbet geçmişi
- Sistem kayıtları

---

## Qdrant

Görevleri

- Embedding saklamak
- Semantic Search yapmak
- En alakalı belge parçalarını bulmak

---

## Ollama / OpenAI

Görevleri

- Kullanıcı sorusunu cevaplamak
- Sadece ilgili belge parçalarını kullanarak cevap üretmek

---

# Doküman İşleme Akışı

1. Kullanıcı belge yükler.
2. Backend belgeyi okur.
3. Belge metne dönüştürülür.
4. Metin küçük parçalara bölünür (Chunking).
5. Her parça embedding'e dönüştürülür.
6. Embedding'ler Qdrant'a kaydedilir.

---

# AI Soru-Cevap Akışı

1. Kullanıcı soru sorar.
2. Soru embedding'e dönüştürülür.
3. Qdrant en alakalı parçaları bulur.
4. Bu parçalar LLM'e gönderilir.
5. LLM cevap üretir.
6. Kaynak bilgisi kullanıcıya gösterilir.

---

# Kullanılan Teknolojiler

Frontend
- React
- Tailwind CSS

Backend
- ASP.NET Core Web API
- Entity Framework Core

Veritabanı
- SQL Server

Vector Database
- Qdrant

Yapay Zekâ
- Ollama
- OpenAI API

Diğer
- Docker
- Git
- Swagger
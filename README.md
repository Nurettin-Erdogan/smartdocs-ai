# 📄 SmartDocs AI

> AI destekli PDF Soru-Cevap Sistemi (RAG Tabanlı)

SmartDocs AI, kullanıcıların PDF belgelerini sisteme yükleyerek bu belgeler üzerinde yapay zekâ destekli soru-cevap yapmasını sağlayan modern bir web uygulamasıdır.

Sistem, yüklenen PDF belgelerini analiz ederek kullanıcıların doğal dilde sordukları sorulara yalnızca ilgili belge içeriklerini kullanarak cevap verir.

Bu proje staj sürecinde **ASP.NET Core**, **React**, **RAG**, **LLM**, **Docker** ve modern yazılım mimarilerini öğrenmek amacıyla **Minimum Viable Product (MVP)** yaklaşımıyla geliştirilmektedir.

---

# 🚀 Proje Amacı

Bu projenin amacı;

- PDF belgelerini sisteme yüklemek
- Belgeleri analiz etmek
- Yapay zekâ ile belge hakkında soru sorabilmek
- Belge içerisinden doğru bilgiyi bulmak
- Cevapların hangi kaynaktan üretildiğini göstermek
- Modern AI teknolojilerini gerçek bir projede kullanmaktır.

---

# 🎯 MVP Özellikleri

- ✅ Kullanıcı girişi
- ✅ PDF yükleme
- ✅ PDF listeleme
- ✅ PDF silme
- ✅ AI destekli soru-cevap
- ✅ Kaynak gösterme
- ✅ Sohbet geçmişi (Opsiyonel)

---

# 🏗️ Sistem Mimarisi

```
                Kullanıcı
                    │
                    ▼
            React Frontend
                    │
                    ▼
        ASP.NET Core Web API
             │            │
             │            │
             ▼            ▼
      SQL Server      Ollama (LLM)
             │            ▲
             │            │
             ▼            │
        PDF Bilgileri     │
                          │
                    Qdrant Vector DB
                          ▲
                          │
                    PDF Embeddingleri
```

---

# 📚 Kullanılan Teknolojiler

## 💻 Frontend

- React
- Tailwind CSS
- Axios

---

## ⚙️ Backend

- ASP.NET Core Web API
- Entity Framework Core

---

## 🗄️ Database

### SQL Server

SQL Server uygulamanın klasik verilerini saklamak için kullanılacaktır.

Örneğin;

- Kullanıcılar
- Giriş bilgileri
- PDF bilgileri
- Dosya yolları
- Yükleme tarihleri
- Sohbet kayıtları

Geliştirme ortamında uygulama ilk kez açıldığında örnek bir admin kullanıcı otomatik oluşturulur.

- E-posta: `admin@smartdocs.ai`
- Şifre: `Admin123!`

---

## 🧠 Artificial Intelligence

### Ollama

Yapay zekâ modeli bilgisayar üzerinde yerel (local) olarak çalışacaktır.

Görevleri;

- Kullanıcı sorularını anlamak
- PDF içerisindeki ilgili bilgileri kullanarak cevap üretmek
- Türkçe doğal cevaplar oluşturmak

---

## 🔍 Vector Database

### Qdrant

Qdrant, PDF belgelerinden oluşturulan **Embedding (Vektör)** verilerini saklamak için kullanılacaktır.

Görevleri;

- PDF içeriklerini embedding'e dönüştürmek
- Anlamsal arama yapmak
- Kullanıcının sorusuna en uygun metin parçalarını bulmak
- Ollama'ya doğru bilgiyi göndermek

---

## 🐳 Container

- Docker

Projenin farklı bilgisayarlarda kolayca çalıştırılabilmesi için kullanılacaktır.

---

## 🌿 Version Control

- Git
- GitHub

Kodların versiyon kontrolü ve ekip çalışması için kullanılacaktır.

---

# 📂 Proje Yapısı

```
smartdocs-ai
│
├── backend
│   └── ASP.NET Core Web API
│
├── frontend
│   └── React Uygulaması
│
├── database
│   └── SQL Scriptleri
│
├── docker
│   └── Docker Dosyaları
│
├── docs
│   └── Proje Dokümantasyonu
│
├── images
│   └── Proje Görselleri
│
├── notes
│   └── Staj Notları
│
└── README.md
```

---

# 🔄 Projenin Çalışma Mantığı

```
1. Kullanıcı giriş yapar

        │

2. PDF yükler

        │

3. Backend PDF'yi okur

        │

4. PDF küçük parçalara ayrılır (Chunking)

        │

5. Her parça Embedding'e dönüştürülür

        │

6. Embedding'ler Qdrant'a kaydedilir

        │

7. Kullanıcı soru sorar

        │

8. Soru Embedding'e dönüştürülür

        │

9. Qdrant en alakalı içerikleri bulur

        │

10. Bulunan içerikler Ollama'ya gönderilir

        │

11. Ollama cevap oluşturur

        │

12. Cevap kullanıcıya gösterilir
```

---

# 📅 Geliştirme Süreci

- [x] Proje planlandı
- [x] GitHub deposu oluşturuldu
- [x] ASP.NET Core Web API kuruldu
- [x] İlk API endpoint oluşturuldu
- [x] Git yapılandırıldı
- [x] Ollama kuruldu
- [ ] SQL Server entegrasyonu
- [ ] Entity Framework kurulumu
- [ ] Kullanıcı sistemi
- [ ] PDF Upload API
- [ ] PDF Parsing
- [ ] Embedding oluşturma
- [ ] Qdrant entegrasyonu
- [ ] AI Chat API
- [ ] React Arayüzü
- [ ] Docker Compose
- [ ] Yayınlama

---

# 🗄️ Veritabanı Notu

Backend uygulaması açıldığında EF Core migration'ları otomatik uygulanır.

Bu yüzden local development sırasında veritabanı yoksa uygulama ilk çalışmada şemayı oluşturur.

İstersen manuel kurulum için [database/SmartDocsAI_Init.sql](database/SmartDocsAI_Init.sql) dosyasını da kullanabilirsin.

---

# 🎯 Öğrenme Hedefleri

Bu proje kapsamında aşağıdaki teknolojilerin öğrenilmesi hedeflenmektedir.

- ASP.NET Core Web API
- React
- Entity Framework Core
- SQL Server
- REST API
- JWT Authentication
- Docker
- Ollama
- Large Language Models (LLM)
- RAG (Retrieval-Augmented Generation)
- Vector Database
- Qdrant
- Git & GitHub
- Katmanlı Mimari (Layered Architecture)

---

# 👨‍💻 Geliştirici

**Nurettin Erdoğan**

Bilgisayar Mühendisliği Öğrencisi

Staj Projesi (2026)

---

# 📌 Proje Durumu

🚧 Geliştirme Devam Ediyor

Bu proje aktif olarak geliştirilmektedir ve MVP sürümü tamamlandıktan sonra yeni özelliklerle geliştirilmeye devam edecektir.
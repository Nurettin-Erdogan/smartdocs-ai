# Dağıtım rehberi

Bu belge Docker Compose ile tek makine kurulumu ve üretime geçerken gereken operasyonel ayarları anlatır.

## Servisler

Varsayılan Compose kurulumu şunları başlatır:

- `app`: React üretim çıktısını da sunan ASP.NET Core uygulaması
- `postgres`: ilişkisel veritabanı
- `qdrant`: vektör veritabanı

Ollama varsayılan olarak ana bilgisayarda beklenir. İstenirse `docker-ai` profiliyle Compose içine alınabilir.

## 1. Ortam dosyası

```powershell
Copy-Item .env.example .env
```

En az şu değerleri girin:

```dotenv
POSTGRES_PASSWORD=uzun-ve-benzersiz-bir-parola
JWT_TOKEN_KEY=en-az-64-baytlik-rastgele-bir-deger
```

`.env` Git tarafından yok sayılır. Gerçek üretimde secret manager veya platform secret özelliği tercih edilmelidir.

JWT anahtarı HMAC-SHA512 nedeniyle en az 64 bayt olmalıdır. Base64 biçiminde 64 rastgele bayt üretme örneği:

```powershell
$bytes = New-Object byte[] 64
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

## 2A. Ana bilgisayardaki Ollama

Modelleri indirin:

```powershell
ollama pull nomic-embed-text
ollama pull llama3
```

`.env` ayarı şöyle kalabilir:

```dotenv
OLLAMA_BASE_URL=http://host.docker.internal:11434
```

Ollama yalnızca `127.0.0.1` üzerinde dinliyorsa uygulama konteyneri ona erişemez. Windows'ta kullanıcı ortam değişkeni olarak `OLLAMA_HOST=0.0.0.0:11434` tanımlayıp Ollama'yı tamamen kapatıp yeniden açın. Güvenlik duvarında 11434 portunu genel ağa açmayın.

Servisleri başlatın:

```powershell
docker compose up --build -d
```

## 2B. Docker içinde Ollama

`.env` dosyasını değiştirin:

```dotenv
OLLAMA_BASE_URL=http://ollama:11434
```

Önce altyapı ve Ollama'yı başlatın:

```powershell
docker compose --profile docker-ai up -d postgres qdrant ollama
docker compose --profile docker-ai exec ollama ollama pull nomic-embed-text
docker compose --profile docker-ai exec ollama ollama pull llama3
```

Ardından uygulamayı ekleyin:

```powershell
docker compose --profile docker-ai up --build -d
```

Bu temel profil CPU ile çalışır. NVIDIA veya AMD GPU geçişi, makinenin sürücülerine göre Compose override dosyasında ayrıca yapılandırılmalıdır.

## 3. Kurulumu doğrulama

```powershell
Invoke-RestMethod http://localhost:8080/api/home
docker compose ps
docker compose logs app --tail 100
```

Tarayıcıda `http://localhost:8080` adresini açın, kayıt olun ve metin içeren bir PDF yükleyin. Belge `Ready` olduğunda soru sorulabilir.

## Kalıcı volume'lar

| Volume | İçerik |
| --- | --- |
| `postgres-data` | Uygulama veritabanı |
| `qdrant-data` | Vektör koleksiyonu |
| `uploads` | Yüklenen PDF dosyaları |
| `ollama-data` | Yalnızca `docker-ai` profilinde modeller |

`docker compose down` volume'ları silmez. `docker compose down -v` tüm kalıcı veriyi siler; yalnızca bilinçli veri sıfırlamada kullanılmalıdır.

## Üretim ayarları

### TLS ve reverse proxy

Konteyner içinde `Hosting__UseHttpsRedirection=false` ayarlıdır; çünkü TLS'in Caddy, Nginx, Traefik veya platform load balancer üzerinde sonlandırılması beklenir. Dış dünyaya yalnızca HTTPS yayınlayın.

Uygulamanın gerçek istemci IP'sini ve dış HTTPS şemasını kullanabilmesi için yalnızca kontrolünüzdeki proxy adreslerini tanımlayın:

```dotenv
ProxySettings__KnownProxies__0=10.0.0.10
```

Uygulama en fazla bir forwarded-header sıçramasını kabul eder. Uygulamayı doğrudan internete açıyorsanız bilinmeyen proxy adreslerini bu listeye eklemeyin.

Frontend ayrı bir API origin'ine bağlanacak şekilde derlenirse varsayılan CSP içindeki `connect-src 'self'` değerine yalnızca o HTTPS API origin'ini ekleyin. Politika `SecurityHeaders__ContentSecurityPolicy` ortam değişkeniyle değiştirilebilir.

### Ortam

`ASPNETCORE_ENVIRONMENT=Production` kullanın. Production ortamında örnek admin hesabı otomatik oluşturulmaz; ilk kullanıcı arayüzden kayıt olabilir.

Development seed'i bilinçli kullanacaksanız:

```dotenv
ASPNETCORE_ENVIRONMENT=Development
SEED_ADMIN_PASSWORD=guclu-bir-gelistirme-parolasi
```

### CORS

Frontend ve API aynı origin'de sunulduğunda ek CORS ayarı gerekmez. Ayrı origin kullanılıyorsa ortam değişkenleriyle dizi elemanlarını tanımlayın:

```dotenv
Cors__AllowedOrigins__0=https://smartdocs.example.com
```

### Ölçekleme

Tek makine Compose kurulumu bir uygulama örneğine yöneliktir. Birden çok API örneğinde:

- migration'ı uygulama başlangıcından ayrı tekil işe taşıyın
- indeksleme kuyruğu için `DocumentIndexingSettings` kiralama, deneme ve gecikme
  değerlerini iş yükünüze göre ayarlayın
- dağıtık kilit kullanın
- `Uploads` için paylaşımlı nesne depolama kullanın
- Qdrant ve PostgreSQL'i yönetilen/yüksek erişilebilir kurun

## Yedekleme ve geri dönüş

PostgreSQL, Qdrant ve `uploads` birlikte yedeklenmelidir. En azından:

```powershell
docker compose exec postgres pg_dump -U smartdocs -d SmartDocsAI_Db -Fc -f /tmp/smartdocs.dump
```

Dump dosyasını konteyner dışına alın ve düzenli geri yükleme tatbikatı yapın. Qdrant için snapshot API'sini, volume'lar için altyapı yedekleme mekanizmasını kullanın.

## Sık sorunlar

### Uygulama Ollama'ya ulaşamıyor

- `OLLAMA_BASE_URL` değerini çalışma biçimine göre kontrol edin.
- Ana bilgisayar kurulumu için Ollama'nın konteyner ağından erişilebilir dinleme adresi kullandığını doğrulayın.
- `docker compose logs app` içinde timeout/connection refused arayın.

### Embedding boyutu hatası

`QDRANT_VECTOR_SIZE`, embedding modelinin ürettiği boyutla eşleşmelidir. Model veya boyut değiştiğinde eski koleksiyonu yedekleyip yeniden oluşturun ve belgeleri yeniden indeksleyin.

### PDF `NoContent` oluyor

PdfPig metin katmanını okur. Yalnızca taranmış görüntü içeren PDF'ler OCR olmadan metin üretmez; OCR mevcut kapsamın dışındadır.

### İlk başlangıç yavaş

İmajların ve Ollama modellerinin ilk indirilmesi büyük olabilir. Sonraki başlangıçlarda Docker layer ve model volume önbelleği kullanılır.

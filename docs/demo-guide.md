# 3 Dakikalık Demo Akışı

Bu senaryo SmartDocs AI'ın RAG akışını yalnızca “PDF'e soru sordum” düzeyinde bırakmadan güvenlik, kaynak gösterimi ve hata yönetimiyle birlikte anlatır.

## Hazırlık

- Canlı Vercel vitrini kullanılacaksa kayıt olmadan yapay zekâ destekli PDF alanını aç.
  PDF tarayıcı içinde işlenir; dosyanın kendisi yüklenmez, yalnızca cevap için seçilen
  metin bölümleri OpenAI API'ye gönderilir.
- Docker Desktop'ı başlat.
- `nomic-embed-text` ve `qwen2.5:3b` modellerinin hazır olduğunu doğrula.
- Kişisel veya gizli belge yerine kısa, paylaşılabilir bir örnek PDF kullan.
- `start-smartdocs.cmd` veya `docker compose up --build` ile sistemi aç.

## 0:00–0:30 — Problem

“Ekipler kendi PDF belgelerinde soru-cevap yapmak istiyor fakat özel veriyi üçüncü taraf servise göndermek istemeyebiliyor. SmartDocs AI, embedding ve cevap üretimini yerelde çalıştırıp cevabı kaynak parçalarıyla birlikte gösteriyor.”

## 0:30–1:15 — Belge akışı

1. Yeni kullanıcı oluştur veya canlı vitrinde “Kendi PDF’inle dene” seçeneğini aç.
2. Örnek PDF'i yükle.
3. `Pending → Ready` indeksleme durumunu göster.
4. Metin içermeyen veya başarısız bir belgenin açık hata durumuna geçtiğini anlat.

Vurgu: yükleme boyutu, PDF imzası, güvenli fiziksel dosya adı ve işlem sınırları API katmanında uygulanır.

## 1:15–2:05 — Kaynaklı soru-cevap

1. Yalnızca belgede yanıtı bulunan net bir soru sor.
2. Cevabın altındaki belge, sayfa, parça ve benzerlik bilgilerini aç.
3. **PDF’de aç** ile ilgili sayfaya git ve yanıtta kullanılan kaynak metnini doğrula.
4. Bir takip sorusu sorarak sohbet bağlamını göster.
5. Yeni sohbet başlatıp geçmiş konuşmanın kalıcı olduğunu göster.

Vurgu: Qdrant araması kullanıcı kimliğiyle filtrelenir; kullanıcı başka hesabın belge veya sohbetine erişemez.

## 2:05–2:40 — Güvenli yeniden indeksleme

1. Yeniden indeksleme akışını göster.
2. Yeni vektörlerin önce ayrı bir sürüm olarak yazıldığını anlat.
3. İşlem başarısız olursa eski çalışan indeksin korunmasını vurgula.
4. Başarılı işlemden sonra aktif sürümün atomik olarak değiştiğini belirt.

## 2:40–3:00 — Kapanış

“Bu projede RAG'i tek bir model çağrısı olarak değil; kimlik doğrulama, sahiplik sınırları, kalıcı veri, güvenli dosya işleme ve geri alınabilir indeksleme içeren ürün akışı olarak geliştirdim.”

## Görüşmede gelebilecek sorular

- Neden PostgreSQL ve Qdrant'ı birlikte kullandın?
- Chunk boyutu ve örtüşme değerlerini nasıl seçtin?
- Hallüsinasyonu kaynak gösterimiyle nasıl sınırlandırdın?
- Yeniden indeksleme yarıda kalırsa veri kaybını nasıl önledin?

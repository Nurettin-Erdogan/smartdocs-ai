# 3 dakikalık demo

SmartDocs AI’ı “PDF’e soru sordum” düzeyinde bırakmadan kaynak, kanıt ve sahiplik sınırlarıyla gösterir.

## Hazırlık

- Canlı vitrin: kayıt olmadan PDF alanını açın. Dosya tarayıcıda kalır; cevap için seçilen metin parçaları Google Gemini’ye gider.
- Tam gizlilik yolu: Docker, `nomic-embed-text` ve `qwen2.5:3b` hazır olsun. Kısa, paylaşılabilir bir örnek PDF kullanın.

## 0:00–0:30 — Problem

Ekipler kendi PDF’lerinde soru-cevap ister; özel metni üçüncü tarafa vermek istemeyebilir. SmartDocs AI yerelde gömme ve üretim yapar, cevabı sayfa ve parça kaynağıyla gösterir. Vitrin yolu aynı arayüzü Gemini ile sunar ve veri akışını açık yazar.

## 0:30–1:15 — Belge

1. Yeni kullanıcı oluşturun veya vitrinde kendi PDF’inizle deneyin.
2. Örnek PDF yükleyin.
3. `Pending → Ready` durumunu gösterin.
4. Metinsiz veya bozuk dosyanın açık hata durumuna geçtiğini anlatın.

Boyut, PDF imzası, rastgele dosya adı ve işlem sınırları API’de uygulanır.

## 1:15–2:05 — Kaynaklı cevap

1. Yalnızca belgede yanıtı olan net bir soru sorun.
2. Kaynak panelinde belge, sayfa, parça ve benzerliği açın.
3. **PDF’de aç** ile alıntıyı doğrulayın.
4. Takip sorusu ve sohbet geçmişini gösterin.

Qdrant araması kullanıcı kimliğiyle süzülür. Başka hesabın belgesine veya sohbetine erişim yoktur. Yerel yığında iddia, belgedeki alıntıyla sunucuda örtüşmezse cevap budanır veya reddedilir.

## 2:05–2:40 — Yeniden indeksleme

1. Yeniden indeksleme kuyruğunu gösterin.
2. Yeni vektörlerin ayrı sürüme yazıldığını anlatın.
3. Başarısızlıkta eski indeksin kaldığını vurgulayın.
4. Başarıda aktif sürümün atomik değiştiğini belirtin.

## 2:40–3:00 — Kapanış

SmartDocs AI, RAG’i tek model çağrısı olarak değil; kimlik, sahiplik, kalıcı sohbet, güvenli dosya işleme ve kanıt denetimi olan bir ürün akışı olarak kurar.

## Olası sorular

**Neden hem PostgreSQL hem Qdrant?**  
PostgreSQL sahiplik, sohbet ve indeks durumunu tutar. Qdrant anlamsal aramayı tutar. Vektör deposu tek başına yetki kaynağı değildir.

**Parça boyutu nasıl seçildi?**  
Kısa alan sorularına yetecek kadar bağlam, uzun kopya yapıştırmayı önleyecek kadar dar. Örtüşme, cümle sınırında kopan iddiaları kurtarır.

**Uydurmayı nasıl sınırlıyorsunuz?**  
Cevap, getirilen parçalara bağlanır. Kanıt doğrulayıcı alıntının kaynak metinde geçmesini ister; yetersiz örtüşmede güvenli ret üretir. Bu, halüsinasyonu sıfırlamaz; temelsiz iddiayı azaltır.

**İndeks yarıda kalırsa?**  
Yeni sürüm ayrı koleksiyondadır. İşlem bitmeden eski sürüm okumaya devam eder; başarıda işaret atomik değişir.

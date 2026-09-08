# Canlı tur

[SmartDocs AI](https://smartdocs-ai-henna.vercel.app) kendi PDF'iniz hakkında soru sormanızı sağlar. Canlı vitrinde PDF tarayıcıda işlenir; AI isteğinde soru, seçilen metin parçaları ve yakın sohbet geçmişi Vercel üzerinden Google Gemini'ye gönderilir. Canlı sürüm tamamen yerel değildir. Yerel işleme için Docker + Ollama + Qdrant kurulumu bulunur.

Kişisel belge yerine kısa, paylaşılabilir bir PDF kullanın.

## Vitrin

1. Kayıt olmadan PDF alanını açın.
2. Kendi PDF'inizi seçin; belge hazır olunca cevabını bildiğiniz net bir soru sorun.
3. Kaynak panelinde belge, sayfa ve alıntıyı görün. **PDF’de aç** ilgili sayfaya gider.
4. Belgede olmayan bir bilgiyi sorun. Modelin bildirdiği iddialara ait alıntılar eşleşmezse yanıt kısaltılabilir veya yetersiz kanıt mesajı gösterilebilir.

## Sonucu nasıl değerlendirmeli?

- Yanıtı doğrudan PDF ile karşılaştırın. Kanıt kontrolü alıntı ve kelime eşleşmesine dayanır; anlamsal doğruluğu garanti etmez.
- AI servisi kullanılamazsa yerel metin yanıtına dönülür. Bunu Gemini yanıtıyla aynı değerlendirmeyin.
- Görüntü tabanlı taramalar için önce OCR gerekir.
- Vitrindeki belgeler kalıcı arşiv değildir; sayfayı yeniledikten sonra yeniden yükleme gerekebilir.

## Yerel yığın

Yükleme boyutu, PDF imzası ve dosya adı API’de denetlenir. Qdrant araması kullanıcı kimliğiyle süzülür. Yeniden indekslemede yeni vektörler ayrı sürüme yazılır; işlem bitmeden eski indeks okumaya devam eder.

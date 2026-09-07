# Canlı tur

[SmartDocs AI](https://smartdocs-ai-henna.vercel.app) PDF üzerinde kaynaklı cevap üretir. Canlı vitrinde dosya tarayıcıda kalır; cevap için seçilen metin parçaları Google Gemini’ye gider. Tam gizlilik için Docker + Ollama + Qdrant kullanılır.

Kişisel belge yerine kısa, paylaşılabilir bir PDF kullanın.

## Vitrin

1. Kayıt olmadan PDF alanını açın.
2. Belgeyi yükleyin; indeks **Ready** olunca net bir soru sorun.
3. Kaynak panelinde belge, sayfa ve alıntıyı görün. **PDF’de aç** ilgili sayfaya gider.
4. Kanıt yetersizse cevap budanır veya reddedilir.

## Yerel yığın

Yükleme boyutu, PDF imzası ve dosya adı API’de denetlenir. Qdrant araması kullanıcı kimliğiyle süzülür. Yeniden indekslemede yeni vektörler ayrı sürüme yazılır; işlem bitmeden eski indeks okumaya devam eder.

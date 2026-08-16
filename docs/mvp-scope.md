# Ürün kapsamı

## Tamamlanan çekirdek kapsam

- kayıt, giriş ve JWT oturumu
- kullanıcıya özel PDF yükleme, listeleme ve silme
- PDF doğrulama, metin çıkarma, chunking ve durum takibi
- Ollama ile embedding ve Türkçe cevap üretimi
- Qdrant ile filtreli anlamsal arama
- belge, sayfa, parça ve skor içeren kaynak gösterimi
- sohbet geçmişi ve takip soruları
- başarısız indekslemeyi yeniden deneme
- Docker Compose, üretim imajı, CI ve otomatik testler

## Bilinçli olarak kapsam dışında

- taranmış PDF'ler için OCR
- Word, Excel ve görsel belge desteği
- belge paylaşımı ve ekip çalışma alanları
- parola sıfırlama/e-posta doğrulama
- gelişmiş yönetim paneli ve denetim kaydı
- mobil uygulama
- çoklu dil arayüzü
- bulut LLM sağlayıcıları

## Üretim öncesi sonraki öncelikler

1. Arka plan iş kuyruğu ve dağıtık yeniden indeksleme kilidi
2. PostgreSQL/Qdrant/Uploads için otomatik yedekleme ve geri yükleme testi
3. OCR iş hattı
4. Entegrasyon ve tarayıcı uçtan uca testleri
5. Gözlemlenebilirlik: yapılandırılmış log, metrik, tracing ve alarm
6. Parola sıfırlama, e-posta doğrulama ve kullanıcı yönetimi

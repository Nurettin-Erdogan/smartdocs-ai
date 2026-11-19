# Değişiklik günlüğü

Önemli değişiklikler bu dosyada sürüm bazında özetlenir. Biçim [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) yaklaşımını izler.

## [Yayınlanmamış]

### Eklendi

- Canlı demo kontrolü (`scripts/live-smoke.ts`, `live-check.yml`): Vercel'deki demo her sabah uçtan uca doğrulanır. Sayfa başlığı ve `frame-ancestors 'none'` CSP'si, Gemini erişimi ve cevabın kaynaktan alıntıyla doğrulanması kontrol edilir; geçici hatada bir kez yeniden denenir. Node 24 betiği derlemeden çalıştırır.
- Canlı kontrolün varsayımlarını gerçek `/api/answer` işleyicisine karşı doğrulayan 6 test.
- Gemini hata verdiğinde loglara HTTP durumuyla birlikte Google RPC durumu (`RESOURCE_EXHAUSTED`, `NOT_FOUND` vb.) yazılır; arızanın kota, model adı ya da anahtar kaynaklı olduğu loglardan ayırt edilebilir. Anahtar ve hata mesajı loglanmaz.

- İndeksleme kuyruğunda çit jetonu (`ProcessingLeaseId`, EF Core eşzamanlılık jetonu) ve heartbeat (`ProcessingHeartbeatAt`): sahipliği kaybeden işçinin yazmaları reddedilir, parçalar belgeyle aynı işlemde yazılır.
- Düzgün kapanışta işçinin belgeyi hemen kuyruğa geri bırakması.
- CI'da PostgreSQL 17 servis konteyneriyle entegrasyon testleri (migration'lar, 8 paralel sahiplenme, çit) ve migration anlık görüntüsü ile model uyum testi.
- Vitrin demosunda Gemini veri akışını ve yerel/Docker ayrımını arayüzde netleştirme.
- README İngilizce özeti, canlı vitrin badge’i ve portföy tablosunda demo/gizlilik satırları.
- Vercel `/api/answer` yanıtında `Retry-After` başlığı.

### Değiştirildi

- Lease süresi 30 dakikadan 5 dakikaya indi: heartbeat sayesinde çöken işçinin belgesi dakikalar içinde devralınır, çalışan işçininki devralınmaz.

### Değiştirildi

- Öğretici `backend/.../Readme.md` dosyaları kaldırıldı.
- Frontend paket sürümü `1.0.0`; paylaşım meta açıklamaları vitrin gerçeğiyle hizalandı.
- Gemini hata loglarından sağlayıcı mesajı temizlendi.

### Eklendi (önceki)

- Katkı rehberi, issue şablonları ve pull request kontrol listesi.
- README üzerinde görünür CodeQL ve lisans kanıtları.

## [1.0.0] - 2026-08-26

### Eklendi

- ASP.NET Core, React, PostgreSQL, Qdrant ve Ollama tabanlı tam RAG akışı.
- JWT kimlik doğrulama ve kullanıcı/veri sahipliği sınırları.
- Kaynak parçası ve sayfa referansı gösteren sohbet deneyimi.
- Güvenli PDF yükleme, durum takibi ve yeniden indeksleme davranışı.
- Sentetik verili canlı vitrin modu ve Docker Compose kurulumu.
- Backend/frontend testleri, CI, CodeQL ve üretim imajı doğrulaması.

[Yayınlanmamış]: https://github.com/Nurettin-Erdogan/smartdocs-ai/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Nurettin-Erdogan/smartdocs-ai/tree/v1.0.0

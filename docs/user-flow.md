# Kullanıcı akışı

```text
Kayıt ol / giriş yap
        ↓
PDF seç ve yükle
        ↓
Pending ── başarısızsa → Failed → Yeniden indeksle
   │
   ├── metin yoksa → NoContent
   │
   └── başarılıysa → Ready
                         ↓
                 Yeni sohbet başlat
                         ↓
                    Soru sor
                         ↓
             Cevap + kaynakları incele
                         ↓
              Takip sorusu veya yeni sohbet
```

## Önemli davranışlar

- Kullanıcı yalnızca kendi belge ve sohbetlerini görür.
- `Ready` olmayan belgeler aramaya katılmaz.
- Eski sohbet seçildiğinde tüm mesajları ayrıca yüklenir.
- Enter soruyu gönderir; Shift+Enter yeni satır ekler.
- JWT geçersiz veya süresi dolmuşsa istemci yerel oturumu temizler ve giriş ekranına döner.
- Belge silmeden önce kalıcı silme onayı istenir.

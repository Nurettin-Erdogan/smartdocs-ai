# SmartDocs AI frontend

React 19, TypeScript ve Vite tabanlı kullanıcı arayüzüdür.

## Geliştirme

```powershell
npm ci
npm run dev
```

Vite `http://localhost:5173` adresinde çalışır. `/api` istekleri varsayılan olarak `http://localhost:5129` backend adresine yönlendirilir.

Farklı API adresi için `.env.example` dosyasını `.env.local` olarak kopyalayın:

```dotenv
VITE_API_BASE_URL=https://api.example.com/api
```

## Kalite kontrolleri

```powershell
npm run typecheck
npm test
npm run build
```

Testler Vitest ile oturum saklama ve API hata ayrıştırma davranışlarını doğrular. Üretim çıktısı `dist` dizinine yazılır; Docker imajı bu çıktıyı ASP.NET Core `wwwroot` dizinine kopyalar.

## Kaynak yapısı

```text
src/App.tsx                         ana ekran ve akışlar
src/api.ts                         tür güvenli HTTP katmanı
src/session.ts                     doğrulanan localStorage oturumu
src/components/ConversationThread  sohbet mesajları
src/components/NotificationBanner  erişilebilir bildirimler
```

# SmartDocs AI frontend

React 19, TypeScript ve Vite tabanlı kullanıcı arayüzü.

## Geliştirme (tam backend’e bağlı)

```powershell
npm ci
npm run dev
```

Vite `http://localhost:5173` adresinde çalışır. `/api` istekleri varsayılan olarak `http://localhost:5129` backend adresine yönlendirilir.

```dotenv
VITE_API_BASE_URL=https://api.example.com/api
```

## Vitrin demosu (Vercel)

```powershell
npm run build:demo
```

`vite build --mode demo` → `frontend/.env.demo` içindeki `VITE_DEMO_MODE=true` ile `isDemoMode` açılır.
Kök `vercel.json` bu komutu kullanır. PDF tarayıcıda (`localPdf.ts` / `demo.ts`) işlenir; cevap için seçilen parçalar `/api/answer` üzerinden Gemini’ye gider.

## Kalite kontrolleri

```powershell
npm run typecheck
npm test
npm run build
```

Vitest; oturum, API hataları, demo akışı ve bağımlılık sürümlerini doğrular.

## Kaynak yapısı

```text
src/App.tsx                         ana ekran ve akışlar
src/api.ts                          HTTP katmanı + isDemoMode köprüsü
src/demo.ts                         vitrin API’si ve Gemini istemcisi
src/localPdf.ts                     tarayıcıda PDF metin çıkarma
src/session.ts                      localStorage oturumu
src/components/ConversationThread   sohbet
src/components/DocumentPreview      sayfa önizleme / vurgu
src/components/NotificationBanner   bildirimler
```

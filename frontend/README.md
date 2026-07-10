# SmartDocs AI Frontend

React 19, TypeScript ve Vite tabanlı arayüzdür. Uygulamanın tek giriş zinciri
`src/main.tsx` → `src/App.tsx` → `src/api.ts` şeklindedir.

## Geliştirme

```powershell
npm install
npm run dev
```

Vite geliştirme sunucusu `http://localhost:5173` adresinde açılır. `/api`
istekleri varsayılan olarak `http://localhost:5129` adresindeki backend'e
yönlendirilir.

## Üretim derlemesi

```powershell
npm run build
```

Çıktı `frontend/dist` klasörüne yazılır. ASP.NET Core backend bu klasör varsa
arayüzü otomatik sunar; klasör yoksa backend yalnızca API olarak çalışmaya devam
eder.

API adresini değiştirmek için `.env.example` dosyasını `.env.local` adıyla
kopyalayıp `VITE_API_BASE_URL` değerini düzenleyebilirsin.

# Katkı rehberi

SmartDocs AI'a yapılacak hata düzeltmeleri, test iyileştirmeleri ve dokümantasyon katkıları memnuniyetle karşılanır. Büyük özellikler için kod yazmaya başlamadan önce bir issue açarak kapsamı netleştirin.

## Geliştirme ortamı

- .NET 10 SDK
- Node.js 24
- Docker Desktop
- Tam RAG akışı için PostgreSQL, Qdrant ve Ollama

Bağımlılıkları kurup temel kontrolleri çalıştırın:

```powershell
dotnet restore tests\SmartDocsAI.API.Tests\SmartDocsAI.API.Tests.csproj
dotnet test tests\SmartDocsAI.API.Tests\SmartDocsAI.API.Tests.csproj --configuration Release

Set-Location frontend
npm ci
npm run typecheck
npm test
npm run build
```

Tam yığın yapılandırmasını doğrulamak için `.env.example` dosyasını temel alıp `docker compose config` komutunu çalıştırabilirsiniz. Parola, token veya gerçek belge verisini commit etmeyin.

## Değişiklik akışı

1. Tek bir probleme odaklanan kısa bir dal oluşturun.
2. Davranış değişiyorsa testi değişiklikle birlikte ekleyin.
3. Kullanıcı akışı veya yapılandırma değişiyorsa README ve ilgili `docs/` belgesini güncelleyin.
4. Pull request açıklamasında problemi, yaklaşımı ve doğrulama adımlarını yazın.

## Güvenlik bildirimleri

Güvenlik açıklarını herkese açık issue olarak paylaşmayın. Bildirim yolu için [SECURITY.md](SECURITY.md) dosyasını kullanın.


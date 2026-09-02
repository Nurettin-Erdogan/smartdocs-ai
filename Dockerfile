# syntax=docker/dockerfile:1.7

FROM node:24-alpine AS frontend-build
WORKDIR /src/frontend

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
# Frontend uç nokta testleri kökteki Vercel fonksiyonunu içe aktarır.
# TypeScript'in çözümleyebilmesi için aynı kaynak düzenini derleme aşamasında koru.
COPY api/ /src/api/
RUN npm run typecheck && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src

COPY backend/SmartDocsAI.API/SmartDocsAI.API.csproj backend/SmartDocsAI.API/
RUN dotnet restore backend/SmartDocsAI.API/SmartDocsAI.API.csproj

COPY backend/SmartDocsAI.API/ backend/SmartDocsAI.API/
RUN dotnet publish backend/SmartDocsAI.API/SmartDocsAI.API.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false

# Program.cs serves publish/wwwroot when it contains a frontend build.
COPY --from=frontend-build /src/frontend/dist/ /out/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

COPY --from=backend-build /out/ ./

RUN mkdir -p Uploads && chown -R "$APP_UID:$APP_UID" Uploads

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "SmartDocsAI.API.dll"]

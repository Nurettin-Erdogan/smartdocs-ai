[CmdletBinding()]
param(
    [switch]$TarayiciyiAc
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker bulunamadı. Önce Docker Desktop kurup çalıştırın.'
}

docker info *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker çalışmıyor. Docker Desktop uygulamasını açıp yeniden deneyin.'
}

function New-RandomSecret([int]$byteCount) {
    $bytes = New-Object byte[] $byteCount
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
}

$environmentPath = Join-Path $PSScriptRoot '.env'
if (-not (Test-Path -LiteralPath $environmentPath)) {
    $databasePassword = New-RandomSecret 32
    $jwtKey = New-RandomSecret 64
    $content = @"
APP_BIND_HOST=127.0.0.1
APP_PORT=8080
ASPNETCORE_ENVIRONMENT=Production
POSTGRES_DB=SmartDocsAI_Db
POSTGRES_USER=smartdocs
POSTGRES_PASSWORD=$databasePassword
JWT_TOKEN_KEY=$jwtKey
SEED_ADMIN_NAME=SmartDocs Admin
SEED_ADMIN_EMAIL=admin@smartdocs.ai
SEED_ADMIN_PASSWORD=
OLLAMA_BASE_URL=http://ollama:11434
OLLAMA_BIND_HOST=127.0.0.1
OLLAMA_PORT=11434
OLLAMA_EMBEDDING_MODEL=nomic-embed-text
OLLAMA_CHAT_MODEL=qwen2.5:3b
OLLAMA_KEEP_ALIVE=-1
OLLAMA_TIMEOUT_SECONDS=0
OLLAMA_NUM_CONTEXT=4096
OLLAMA_TEMPERATURE=0.1
QDRANT_COLLECTION=smartdocs_chunks
QDRANT_VECTOR_SIZE=768
"@
    [IO.File]::WriteAllText(
        $environmentPath,
        $content.Trim() + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
    Write-Host 'Güvenli .env ayarları oluşturuldu.' -ForegroundColor Green
}

Write-Host 'SmartDocs AI hazırlanıyor...' -ForegroundColor Cyan
docker compose up -d --build
if ($LASTEXITCODE -ne 0) {
    throw 'Docker servisleri başlatılamadı.'
}

Write-Host 'Yerel yapay zekâ modelleri hazırlanıyor...' -ForegroundColor Cyan
foreach ($requiredModel in @('nomic-embed-text', 'qwen2.5:3b')) {
    docker compose exec -T ollama ollama pull $requiredModel
    if ($LASTEXITCODE -ne 0) {
        throw "Ollama modeli hazırlanamadı: $requiredModel"
    }
}

$portLine = Get-Content -LiteralPath $environmentPath |
    Where-Object { $_ -match '^APP_PORT=' } |
    Select-Object -First 1
$applicationPort = if ($portLine) { ($portLine -split '=', 2)[1].Trim() } else { '8080' }
$applicationUrl = "http://localhost:$applicationPort"
$healthUrl = "$applicationUrl/api/home/ready"
$ready = $false
for ($attempt = 1; $attempt -le 30; $attempt++) {
    try {
        $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 5
        if ($health.status -eq 'hazır') {
            $ready = $true
            break
        }
    } catch {
        Start-Sleep -Seconds 2
    }
}

if ($ready) {
    Write-Host "SmartDocs AI hazır: $applicationUrl" -ForegroundColor Green
} else {
    Write-Warning "Uygulama başlatıldı ancak tüm servisler henüz hazır değil. Durum: $healthUrl"
}

if ($TarayiciyiAc) {
    Start-Process $applicationUrl
}

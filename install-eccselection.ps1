$ErrorActionPreference = "Stop"

$InstallDir = Join-Path $env:USERPROFILE "ECCSELECTION"
$BundleUrl = "https://devon119.github.io/ecc-harness-studio/downloads/eccselection-full.zip"
$ZipPath = Join-Path $env:TEMP "eccselection-full.zip"

Write-Host "=== ECCSELECTION 完整版 (.NET 10) 一鍵安裝程式 ===" -ForegroundColor Cyan

# 1. Check .NET 10 SDK
$dotnetVersion = $null
try {
    $dotnetVersion = (dotnet --version 2>$null)
} catch {
    $dotnetVersion = $null
}

if (-not $dotnetVersion -or -not $dotnetVersion.StartsWith("10.")) {
    Write-Host "未偵測到 .NET 10 SDK，嘗試透過 winget 安裝..." -ForegroundColor Yellow
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        winget install Microsoft.DotNet.SDK.10 --accept-package-agreements --accept-source-agreements
    } else {
        Write-Error "請先安裝 .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0) 後再執行此腳本。"
        exit 1
    }
} else {
    Write-Host "已偵測到 .NET SDK: $dotnetVersion" -ForegroundColor Green
}

# 2. Download & Extract Full Edition
Write-Host "正在下載完整版套件至 $ZipPath ..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $BundleUrl -OutFile $ZipPath -UseBasicParsing

if (Test-Path $InstallDir) {
    Write-Host "更新既有安裝目錄：$InstallDir" -ForegroundColor Yellow
} else {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

Expand-Archive -Path $ZipPath -DestinationPath $InstallDir -Force
Remove-Item $ZipPath -Force -ErrorAction SilentlyContinue

Write-Host "安裝完成！正在啟動 ECCSELECTION (http://localhost:5109)..." -ForegroundColor Green
Start-Process "http://localhost:5109"
Set-Location $InstallDir
dotnet run --project src\ECCSELECTION.Web\ECCSELECTION.Web.csproj --urls http://localhost:5109

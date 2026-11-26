#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR="$HOME/ECCSELECTION"
BUNDLE_URL="https://devon119.github.io/ecc-harness-studio/downloads/eccselection-full.zip"
ZIP_PATH="/tmp/eccselection-full.zip"

echo "=== ECCSELECTION 完整版 (.NET 10) 一鍵安裝程式 ==="

if ! command -v dotnet >/dev/null 2>&1; then
  echo "錯誤：請先安裝 .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0)。"
  exit 1
fi

DOTNET_VER=$(dotnet --version || true)
echo "偵測到 .NET SDK: $DOTNET_VER"

echo "正在下載完整版套件..."
curl -fSL "$BUNDLE_URL" -o "$ZIP_PATH"

mkdir -p "$INSTALL_DIR"
unzip -o "$ZIP_PATH" -d "$INSTALL_DIR" >/dev/null
rm -f "$ZIP_PATH"

echo "安裝完成！正在啟動 http://localhost:5109 ..."
cd "$INSTALL_DIR"
if command -v open >/dev/null 2>&1; then
  (sleep 2 && open "http://localhost:5109") &
elif command -v xdg-open >/dev/null 2>&1; then
  (sleep 2 && xdg-open "http://localhost:5109") &
fi

dotnet run --project src/ECCSELECTION.Web/ECCSELECTION.Web.csproj --urls http://localhost:5109

$ErrorActionPreference = "Stop"

Write-Host "=== [1/4] dotnet build (0 warnings, 0 errors) ===" -ForegroundColor Cyan
dotnet build ECCSELECTION.slnx -warnaserror
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "`n=== [2/4] dotnet format --verify-no-changes ===" -ForegroundColor Cyan
dotnet format ECCSELECTION.slnx --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet format verification failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "`n=== [3/4] dotnet test + XPlat Code Coverage ===" -ForegroundColor Cyan
dotnet test ECCSELECTION.slnx --no-build --verbosity normal --collect:"XPlat Code Coverage"
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet test failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "`n=== [4/4] ecc-agentshield security scan ===" -ForegroundColor Cyan
npx --yes ecc-agentshield scan --path . --format text
if ($LASTEXITCODE -ne 0) {
    Write-Error "ecc-agentshield scan failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "`n=== ALL QUALITY GATE CHECKS PASSED ===" -ForegroundColor Green

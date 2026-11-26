$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $PSScriptRoot
$OutputDir = Join-Path $RootDir "docs\downloads"
$OutputZip = Join-Path $OutputDir "eccselection-full.zip"
$StagingDir = Join-Path $env:TEMP ("eccselection-stage-" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

try {
    $includeItems = @(
        "ECCSELECTION.slnx",
        "AGENTS.md",
        "SPEC_ECCSELECTION.md",
        "DEVELOPMENT_STATUS.md",
        "README.md",
        "src",
        "tests",
        "scripts",
        ".ecc",
        ".agents"
    )

    foreach ($item in $includeItems) {
        $srcPath = Join-Path $RootDir $item
        if (Test-Path $srcPath) {
            Copy-Item -Path $srcPath -Destination (Join-Path $StagingDir $item) -Recurse -Force
        }
    }

    # Remove build artifacts and local secret files from staging
    Get-ChildItem -Path $StagingDir -Recurse -Directory -Include bin, obj, TestResults | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $StagingDir -Recurse -File -Filter "appsettings.Development.local.json" | Remove-Item -Force -ErrorAction SilentlyContinue

    if (Test-Path $OutputZip) {
        Remove-Item $OutputZip -Force
    }

    Compress-Archive -Path (Join-Path $StagingDir "*") -DestinationPath $OutputZip -CompressionLevel Optimal
    Write-Host "Built full edition package: $OutputZip" -ForegroundColor Green
}
finally {
    if (Test-Path $StagingDir) {
        Remove-Item $StagingDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

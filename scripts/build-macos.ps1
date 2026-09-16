param([string]$Dotnet, [string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Dotnet) {
    $localSdk = Join-Path $env:LOCALAPPDATA 'DialShift/sdk/dotnet.exe'
    $Dotnet = if (Test-Path $localSdk) { $localSdk } else { 'dotnet' }
}
Push-Location $root
try {
    & $Dotnet run --project DialShift.Tests -c Release
    if ($LASTEXITCODE) { throw 'Core checks failed' }
    & $Dotnet run --project DialShift.Desktop.Tests -c Release
    if ($LASTEXITCODE) { throw 'Desktop checks failed' }
    $toolFolder = Join-Path $root '.tools/apple-codesign-0.29.0'
    $signer = Join-Path $toolFolder 'apple-codesign-0.29.0-x86_64-pc-windows-msvc/rcodesign.exe'
    if (!(Test-Path $signer)) {
        New-Item -ItemType Directory -Force (Join-Path $root '.tools') | Out-Null
        $archive = Join-Path $root '.tools/apple-codesign-0.29.0.zip'
        Invoke-WebRequest 'https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign/0.29.0/apple-codesign-0.29.0-x86_64-pc-windows-msvc.zip' -OutFile $archive
        if ((Get-FileHash $archive -Algorithm SHA256).Hash -ine '54BB500E2DA7A8DE02FCAE0F331D1CAC6E6D7173B4281042FF9C528BA3159AAA') { throw 'Signing tool checksum mismatch' }
        Expand-Archive -LiteralPath $archive -DestinationPath $toolFolder -Force
    }
    # Fresh output for each build avoids stale files and never touches the running Windows app.
    $publish = Join-Path $root ('artifacts/mac-publish-' + [Guid]::NewGuid().ToString('N'))
    & $Dotnet publish DialShift.Desktop -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $publish
    if ($LASTEXITCODE) { throw 'Mac publish failed' }
    & $Python scripts/package-macos.py --publish $publish --signer $signer
    if ($LASTEXITCODE) { throw 'Mac packaging failed' }
} finally { Pop-Location }

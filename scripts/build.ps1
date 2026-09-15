param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $env:LOCALAPPDATA 'DialShift\sdk\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
if (-not $SkipTests) {
    & $dotnet run --project (Join-Path $root 'DialShift.Tests') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core checks failed.' }
}
$output = Join-Path $root 'artifacts\DialShift-win-x64'
& $dotnet publish (Join-Path $root 'DialShift\DialShift.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $root 'README.md'),(Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $output
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $output -Recurse -Force
$archive = Join-Path $root 'artifacts\DialShift-0.1.0-win-x64.zip'
Compress-Archive -Path "$output\*" -DestinationPath $archive -Force
Write-Host "Built: $output\DialShift.exe"
Write-Host "Archive: $archive"

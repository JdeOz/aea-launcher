param([string]$SigningKey=(Join-Path $env:LOCALAPPDATA 'AeaPublisher\release-signing.bin'))
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$localSdk=Join-Path (Split-Path -Parent $projectRoot) '.tools\dotnet\dotnet.exe'
$dotnetTool=if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
Push-Location $projectRoot
try {
    $pack=Get-Content -LiteralPath 'pack.json' -Raw | ConvertFrom-Json
    if ($pack.version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Versión de pack inválida.' }
    $destination=Join-Path $projectRoot ('artifacts\exports\' + $pack.version)
    if (Test-Path -LiteralPath $destination) { throw 'Esta versión ya fue exportada. Incrementa la versión para publicar otro contenido.' }
    & $dotnetTool run --project tools\Aea.Publisher -- pack pack.json $SigningKey $destination
    if ($LASTEXITCODE -ne 0) { throw 'La exportación no se completó.' }
    Write-Output "Exportación local: $destination"
    Write-Output 'Publica release.json como asset de GitHub Releases en el canal configurado. Este script no publica ni envía archivos.'
} finally { Pop-Location }

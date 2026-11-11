param([switch]$SkipTests)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$localSdk=Join-Path (Split-Path -Parent $projectRoot) '.tools\dotnet\dotnet.exe'
$dotnetTool=if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
Push-Location $projectRoot
try {
    & $dotnetTool restore Aea.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron restaurar las dependencias fijadas.' }
    & $dotnetTool build Aea.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }
    if (-not $SkipTests) {
        & $dotnetTool run --project tests\Aea.Tests -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Las pruebas fallaron.' }
    }
    & $dotnetTool publish src\Aea.App -c Release --no-restore -o artifacts\release
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo generar el ejecutable portátil.' }
    $binary=Join-Path $projectRoot 'artifacts\release\Aea.exe'
    $smokeDirectory=Join-Path $projectRoot ('artifacts\smoke-' + [Guid]::NewGuid().ToString('N'))
    $uiProcess=Start-Process -FilePath $binary -ArgumentList @('--smoke',('"' + $smokeDirectory + '"')) -PassThru
    if (-not $uiProcess.WaitForExit(30000)) { throw 'La interfaz no terminó su prueba en 30 segundos.' }
    if ($uiProcess.ExitCode -ne 0) { throw "La prueba del ejecutable final falló. Revisa $smokeDirectory" }
    if (-not (Test-Path -LiteralPath (Join-Path $smokeDirectory 'smoke.json'))) { throw 'No se generó evidencia de la prueba de interfaz.' }
    $extraFiles=Get-ChildItem -LiteralPath (Split-Path -Parent $binary) -File | Where-Object Name -ne 'Aea.exe'
    if ($extraFiles) { throw 'La publicación contiene archivos adicionales al ejecutable.' }
    Write-Output "Ejecutable portátil: $binary"
    Write-Output "Evidencia de interfaz: $smokeDirectory"
} finally { Pop-Location }

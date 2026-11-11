param(
    [string]$MicrosoftClientId='',
    [string]$UpdateUrl='',
    [string]$PublicKeyFile=(Join-Path $env:LOCALAPPDATA 'AeaPublisher\release-signing.bin.public.pem')
)
$ErrorActionPreference='Stop'
if ($MicrosoftClientId -and -not [Guid]::TryParse($MicrosoftClientId,[ref]([Guid]::Empty))) { throw 'Client ID inválido.' }
if ($UpdateUrl -and $UpdateUrl -notmatch '^https://(github\.com|raw\.githubusercontent\.com)/') { throw 'Configura un canal HTTPS de GitHub.' }
$brand=[ordered]@{ name='Aea'; microsoftClientId=$MicrosoftClientId; updateUrl=$UpdateUrl; publicKey=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $PublicKeyFile).Path) }
$destination=Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Aea.App\Resources\brand.json'
[IO.File]::WriteAllText($destination,($brand | ConvertTo-Json -Depth 3),[Text.UTF8Encoding]::new($false))
Write-Output 'Configuración del proyecto actualizada. Recompila antes de distribuir el ejecutable.'

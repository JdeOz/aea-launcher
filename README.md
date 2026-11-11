# Aea Launcher

Launcher Windows x64 escrito desde cero en C#/.NET10 y WPF. No contiene Helios, Electron,
Node ni assets/configuración/perfiles importados del prototipo anterior. Interfaz mínima,
sin logos ni ilustraciones: cuenta, RAM máxima y Jugar/Actualizar.

Proyecto independiente, no oficial ni afiliado con Mojang o Microsoft. Los archivos del
juego se descargan de los proveedores oficiales; el ejecutable no incluye una copia del juego.

## Estado real

La aplicación Microsoft y el canal de actualizaciones están deliberadamente sin configurar.
No se ha probado una sesión real ni el arranque autenticado: requieren Client ID propio con
redirección de escritorio y aprobación de Minecraft. El programa explica esa situación y
no construye cuentas offline ni usa identidades de otros launchers.

Los tests de autenticación usan respuestas simuladas. La preparación real verifica Java,
metadatos oficiales, bibliotecas, recursos, mods y carga de la clase principal con Java
`--dry-run`; esa comprobación no ejecuta el juego ni prueba una cuenta.

## Ejecutable

`artifacts/release/Aea.exe` es la publicación autónoma de un solo archivo. Se puede mover a
otra carpeta y abrir sin instalador, sin .NET preinstalado y sin carpetas del proyecto.
El runtime nativo puede extraerse automáticamente en el temporal del usuario. Los datos
del juego se guardan en `%LOCALAPPDATA%/AeaLauncher`, separados del ejecutable y de otros
launchers. No significa que todos los archivos del juego estén dentro del .exe.

El binario no tiene firma Authenticode; Windows puede mostrar una advertencia de editor
desconocido. Las firmas ECDSA de los packs verifican actualizaciones; no sustituyen la firma
de Windows ni garantizan que un mod sea seguro.

## Compilar y verificar

Instala el SDK de global.json o usa `.tools/dotnet` en el directorio padre (la preparación
local descargó el ZIP oficial y comprobó SHA512). Desde PowerShell:

```powershell
./scripts/build.ps1
```

Restaura paquetes fijados, compila sin advertencias, ejecuta el harness de regresión,
publica el exe y prueba la interfaz del propio binario con un perfil temporal. El modo
`--smoke <carpeta>` comprueba controles, RAM, errores y genera capturas; no autentica ni abre
Minecraft. Logs y resultados están en la carpeta que imprime el script.

## Configuración de administrador

1. Registra una aplicación de escritorio propia para cuentas Microsoft. Habilita la
   redirección `http://localhost`; MSAL usa el navegador del sistema, PKCE y puerto local.
   No pongas un client secret dentro de la aplicación.
2. Intenta iniciar sesión con esa identidad y solicita la revisión de AppID en
   https://aka.ms/mce-reviewappid. La aprobación depende de Microsoft.
3. Configura el Client ID, el canal y la clave pública, y recompila:

```powershell
./scripts/configure.ps1 -MicrosoftClientId 'GUID-PROPIO' `
  -UpdateUrl 'https://github.com/USUARIO/REPO/releases/latest/download/release.json'
./scripts/build.ps1
```

Ni el Client ID ni la clave pública son secretos. La clave privada de firma no se distribuye.
La solicitud externa y la publicación no forman parte de los scripts ni se han realizado.

## Publicar cambios de mods

Edita `pack.json`: incrementa `version` y cambia la lista `mods` (slugs de Modrinth).
`./scripts/export-pack.ps1` resuelve versiones estables compatibles y dependencias requeridas,
fija hashes y exporta `release.json` firmado y `pack.lock.json`. Una dependencia incompatible
o una firma inválida detienen el proceso. Publica el release firmado en el canal HTTPS
configurado. Los jugadores verán Actualizar al volver a abrir; nada se instala antes de pulsarlo.
El workflow de GitHub preparado verifica builds; no publica automáticamente ni contiene claves.

La clave nueva se creó cifrada para tu usuario de Windows en
`%LOCALAPPDATA%/AeaPublisher/release-signing.bin`; la pública está al lado. Conserva la clave
y el acceso al perfil Windows que la protege: si se pierde, habrá que distribuir un launcher
con una clave pública nueva. No guardes la privada ni cachés de cuenta en GitHub.

Para otro proyecto, usa otro packId, carpeta de datos y clave/canal; recompila su distribución.
Las versiones del launcher y del pack son independientes. Cambiar el programa requiere otro exe.

## Arquitectura y seguridad

- `Aea.Core`: módulos de autenticación, descargas, releases, almacenamiento y arranque.
- `Aea.App`: WPF nativo sin HTML remoto ni puentes de ejecución JavaScript.
- `Aea.Publisher`: administración y exportación local; nunca forma parte del exe de jugadores.
- `Aea.Tests`: harness de regresión, ejecutable con código de salida distinto de cero si falla.
- Sesiones Microsoft cifradas con DPAPI CurrentUser; tokens Xbox/Minecraft solo en memoria.
- Actualizaciones firmadas ECDSA P256/SHA256. Metadatos y artefactos con hashes fijados;
  Minecraft conserva SHA1 oficial para integridad, no para firma ni autenticación.
- Descargas HTTPS con redirecciones restringidas, archivos temporales y verificación antes
  de mover. Bibliotecas y Java por hash; releases por digest; puntero activo atómico.
- Los mundos y opciones no se reemplazan. Mods administrados se cargan desde la release
  con `fabric.addMods`; al retirarlos del pack ya no se incluyen en el arranque.
- Autorización, licencia activa de Java y perfil se comprueban antes de cada arranque; una cuenta guardada
  no sustituye esa comprobación. No se borran sesiones por un fallo de red.
- Diagnósticos con etapa/tipo/estado HTTP; sin cuerpos HTTP, tokens, nombres, correos ni
  argumentos de lanzamiento. Minecraft mantiene sus propios logs en la instancia.

## Fuentes

- https://learn.microsoft.com/dotnet/core/deploying/single-file/overview
- https://learn.microsoft.com/entra/msal/dotnet/acquiring-tokens/using-web-browsers
- https://learn.microsoft.com/dotnet/standard/security/how-to-use-data-protection
- https://github.com/FabricMC/fabric-meta
- https://docs.modrinth.com/api/operations/getprojectversions/

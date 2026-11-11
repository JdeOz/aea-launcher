# Verificación local — 2026-11-11

## Resultado

- .NET10.0.404, Release build: cero advertencias y cero errores.
- 19/19 escenarios de regresión pasan. Firmas alteradas, claves ajenas, rutas Windows,
  zip slip/enlaces/duplicados, hashes corruptos, cancelación, feed incorrecto, downgrade,
  permisos de Minecraft, sesión dañada, RAM, preservación de mundos y actualización exitosa.
  La autenticación requiere licencia activa game_minecraft además del perfil de Java; una
  licencia ausente o un servicio de licencias no disponible bloquean el arranque.
- Regresiones adicionales comprueban el formato JSON de Xbox y la lista NBT inicial de
  servidores, sin reemplazar listas existentes ni añadir argumentos de conexión automática.
- Auditoría NuGet de los cuatro proyectos, incluidas dependencias transitivas: ninguna
  vulnerabilidad conocida reportada. No constituye una auditoría independiente de seguridad.
- Interfaz del exe final: abre, persiste RAM y presenta un error reintentable. Capturas en
  la carpeta `artifacts/smoke-*` indicada por cada compilación. El HTTP403 de esa prueba es simulado.
- Copia de solo Aea.exe en una carpeta con espacios: smoke pasa con DOTNET_ROOT apuntando
  a una carpeta inexistente; sin archivos DLL o recursos vecinos.
- Preparación real nueva: Java25, Minecraft26.3, Fabric0.20.1, 82 bibliotecas, 5149 recursos,
  seis mods. Artefactos descargados y hashes comprobados.
- Java `--dry-run` acepta el comando y carga la clase principal sin ejecutar el juego.
- Exportación inicial local firmada; la clave privada se cifró con DPAPI fuera del repo.

## Límites pendientes

- La aplicación Microsoft propia está registrada y `brand.json` contiene su Client ID público.
  El canal remoto de packs sigue vacío. No se reutiliza la configuración de Helios.
- Prueba real de cuenta: Microsoft, Xbox Live y XSTS completados; Minecraft devolvió HTTP403.
  Minecraft real, renovación de sesión real y partida autenticada siguen sin verificar.
- Solicitud de aprobación de AppID enviada y confirmada el 11 de noviembre de 2026.
  La aprobación depende del proveedor y no está concedida todavía.
- La lógica de feed/update fue probada con respuestas simuladas y artefactos de prueba;
  falta un canal real de packs. La publicación del ejecutable no habilita ese canal.
- No se dispone de una segunda PC limpia para validar el binario en otro Windows.
- El exe no tiene firma Authenticode. Windows puede mostrar editor desconocido.
- El servidor no se encendió ni se modificó durante la preparación de esta versión.

## Reproducir

`scripts/build.ps1` compila, ejecuta las regresiones, publica el exe y prueba su interfaz.
`Aea.Publisher prepare <release.json> <public.pem> <datos>` vuelve a verificar los archivos
reales y ejecuta Java dry-run sin abrir el juego. No sustituyen el ensayo autenticado.

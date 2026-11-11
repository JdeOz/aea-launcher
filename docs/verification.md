# Verificación local — 2026-11-11

## Resultado

- .NET10.0.404, Release build: cero advertencias y cero errores.
- 17/17 escenarios de regresión pasan. Firmas alteradas, claves ajenas, rutas Windows,
  zip slip/enlaces/duplicados, hashes corruptos, cancelación, feed incorrecto, downgrade,
  permisos de Minecraft, sesión dañada, RAM, preservación de mundos y actualización exitosa.
  La autenticación requiere licencia activa game_minecraft además del perfil de Java; una
  licencia ausente o un servicio de licencias no disponible bloquean el arranque.
- Auditoría NuGet de los cuatro proyectos, incluidas dependencias transitivas: ninguna
  vulnerabilidad conocida reportada. No constituye una auditoría independiente de seguridad.
- Interfaz del exe final: abre, persiste RAM y presenta un error reintentable. Capturas en
  artifacts/smoke-9347c2a23bab4aa2a85f5ebdeb25e094. El HTTP403 de esa prueba es simulado.
- Copia de solo Aea.exe en una carpeta con espacios: smoke pasa con DOTNET_ROOT apuntando
  a una carpeta inexistente; sin archivos DLL o recursos vecinos.
- Preparación real nueva: Java25, Minecraft26.3, Fabric0.20.1, 82 bibliotecas, 5149 recursos,
  seis mods. Artefactos descargados y hashes comprobados.
- Java `--dry-run` acepta el comando y carga la clase principal sin ejecutar el juego.
- Exportación inicial local firmada; la clave privada se cifró con DPAPI fuera del repo.

## Límites pendientes

- brand.json no tiene Client ID ni canal remoto. No se reutiliza la configuración de Helios.
- MSAL/Minecraft real, renovación de sesión real y partida autenticada no verificados.
- Aprobación de Microsoft no solicitada para el proyecto nuevo; depende del proveedor.
- No se publicó el código, la página, un pack ni el workflow. La lógica de feed/update fue
  probada con respuestas simuladas y artefactos de prueba; falta una publicación real.
- No se dispone de una segunda PC limpia para validar el binario en otro Windows.
- El exe no tiene firma Authenticode. Windows puede mostrar editor desconocido.
- El servidor permaneció apagado y no se modificó durante esta tarea.
- La revisión automática rechazó borrar el proyecto antiguo `launcher` y sus datos locales
  `.aeatenico` y `AeaTenico Launcher` de Roaming. Solo indicó bloqueo por política. Siguen en
  disco, fuera de la solución nueva y sin referencias desde ella.

## Reproducir

`scripts/build.ps1` compila, ejecuta las regresiones, publica el exe y prueba su interfaz.
`Aea.Publisher prepare <release.json> <public.pem> <datos>` vuelve a verificar los archivos
reales y ejecuta Java dry-run sin abrir el juego. No sustituyen el ensayo autenticado.

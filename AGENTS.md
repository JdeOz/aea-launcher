# Aea Launcher

Windows x64, C#/.NET 10 LTS, WPF. Proyecto nuevo: no copiar código, assets, perfiles ni
configuración del prototipo Helios. No tocar ni encender el servidor.

La UI solo presenta cuenta Microsoft, RAM máxima y Jugar/Actualizar/Cancelar. Sin dibujos.
Código de dominio en Aea.Core; WPF no procesa HTML ni ejecuta contenido remoto.
Tokens únicamente en memoria o caché MSAL cifrada con DPAPI para CurrentUser.
No registrar cuerpos HTTP, cabeceras, credenciales ni argumentos de lanzamiento completos.
Actualizaciones de pack firmadas, archivos verificados, cambio de versión atómico.
No reutilizar Client IDs ajenos. Falta de configuración/aprobación se comunica explícitamente.
No afirmar login ni juego verificados hasta una prueba real autorizada. No publicar sin autorización.
SDD: intención vigente en specs; cambios de contrato actualizan spec/plan/tasks y análisis antes
de código. Bugs internos: diagnóstico y comprobaciones proporcionadas. Sin código legacy paralelo.

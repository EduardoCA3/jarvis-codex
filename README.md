# Jarvis Codex nativo

Asistente personal para Windows con interfaz, micrófono y controles nativos en
C#/.NET. Escucha **“Hey Jarvis”** automáticamente mientras está abierto, usa
Vosk para detectar el nombre y Whisper Small para entender el español. Consulta
Codex solo cuando la petición no es una acción local conocida.

## Preparar y ejecutar desde el código fuente

Este repositorio contiene el código fuente; no incluye binarios generados ni
modelos de voz, que son demasiado grandes para versionarlos con Git. En Windows
instala el SDK de .NET 10 y Python, crea el entorno de Python con
`INSTALAR.bat`, y después ejecuta:

```powershell
dotnet run --project native-src\Jarvis.Native\Jarvis.Native.csproj
```

Para la voz local configura `JARVIS_VOICE_MODEL` con la ruta a un modelo Vosk
en español y `JARVIS_WHISPER_MODEL` con la ruta al archivo Whisper GGML. Los
artefactos compilados se publican por separado como una versión de lanzamiento.

No aparece una consola. Al cerrar la ventana, Jarvis se oculta en la bandeja y
sigue escuchando. Para cerrarlo por completo, usa **Salir** en el icono de la
bandeja. La escucha continua permanece activa como se solicitó.

Para hablar:

Puedes decir nombre y orden en una frase: **“Jarvis abre la calculadora”**.
También puedes decir solo **“Jarvis”**, esperar la respuesta **“Dime”** y hablar.
Las conversaciones que no comienzan por el nombre se ignoran y no aparecen en
la interfaz. Después de la primera respuesta aparece **Conversación activa**:
durante 2 minutos puedes seguir hablando sin repetir `Jarvis`; cada respuesta
renueva el tiempo. Di **“Jarvis, finalizar”**, **“termina la conversación”** o
**“eso es todo”** para cerrarla inmediatamente. Desde ese momento ignorará la
conversación y solo reaccionará cuando vuelvas a decir su nombre.

## Cambiar el nombre y la activación

Pulsa **EDIT** junto al nombre, escribe uno nuevo —por ejemplo `Alex`— y
pulsa **GUARDAR**. La interfaz, la voz y la personalidad de Codex adoptan el
nuevo nombre inmediatamente y la elección queda guardada en el PC.

El detector acepta el nombre nuevo y sus formas `Hey <nombre>` y `Oye <nombre>`.
El nombre anterior deja de activar al asistente. Se admiten entre 2 y 20
caracteres: letras, espacios y guiones.

## Interfaz tecnológica

La ventana incluye un núcleo animado, visualizador de voz, estados del motor,
indicadores de Terra y solo lectura, tarjetas de conversación y accesos rápidos.
Los colores cambian según esté escuchando, capturando, procesando, hablando, en
pausa o en error.

## Reconocimiento offline

La voz se procesa localmente con dos motores. Vosk mantiene una escucha ligera
para detectar el nombre. Después, Whisper Small multilingüe acelerado por la
RTX mediante Vulkan transcribe la frase completa en español. No necesita
reconocimiento en línea, cuenta externa ni clave. Configura las rutas de los
modelos mediante `JARVIS_VOICE_MODEL` y `JARVIS_WHISPER_MODEL`; el micrófono
se selecciona en la configuración de la aplicación.

La respuesta hablada usa explícitamente `Microsoft Raul - Spanish (Mexico)`.
Las respuestas de Codex están fijadas a español latino; solo cambia de idioma
cuando se solicita una traducción.

Como protección adicional, una capa fonética corrige errores residuales en
nombres de aplicaciones y compara de forma tolerante contra el catálogo real
de programas instalados.

## Coste y seguridad

- La activación Vosk y el dictado Whisper son offline: no tienen coste por uso
  ni necesitan claves API.
- No usa `OPENAI_API_KEY`, `CODEX_API_KEY` ni facturación por API.
- El único modelo permitido es `gpt-5.6-terra`; Luna está bloqueado.
- Codex usa la sesión de ChatGPT/Codex disponible y modo de solo lectura.
- Si se alcanza el límite incluido, Jarvis informa del límite y no cambia a una
  API ni a un modelo de pago alternativo.
- Las acciones locales no destructivas usan el catálogo nativo de aplicaciones
  y las carpetas personales. Borrados, compras, instalaciones, mensajes,
  apagado y reinicio están bloqueados por voz para evitar accidentes.

Tu pago de ChatGPT Plus no convierte la API en gratuita: por eso este proyecto
no usa la API. Las consultas complejas sí dependen de que tu cuenta tenga cuota
de Codex disponible dentro de tu plan.

## Órdenes locales

- `ayuda`
- `qué hora es` / `qué fecha es`
- `abre bloc de notas`, `abre calculadora`, `abre Paint`, `abre terminal`
- `abre Spotify` / `reproducir` / `pausa` / `siguiente canción`
- `abre Firefox`, `abre Discord`, `abre Steam`, `abre CapCut` o cualquier app
  del catálogo de aplicaciones de Windows
- `abre mis Descargas`, `abre Documentos`, `abre Escritorio`, `abre Este equipo`
- `abre <nombre de archivo>` o `busca el archivo <nombre>` dentro de tus
  carpetas personales
- `abre configuración de Bluetooth`, `Wi-Fi`, `pantalla`, `sonido` o
  `Windows Update`
- `minimiza la ventana`, `maximiza la ventana`, `restaura la ventana`
- También entiende `quiero que me abras...`, `puedes abrir...` o `inicia...`
- `abre youtube`, `abre google`, `busca restaurantes en Lima`
- `anota comprar leche` / `mis notas`
- `recuerda que mi color favorito es azul` / `qué recuerdas`
- `sube el volumen`, `baja el volumen`, `silencio`, `pausa`
- `estado del sistema`
- `Jarvis, finalizar` / `termina la conversación` / `eso es todo`

Los datos nuevos de la versión nativa se guardan en
`%LOCALAPPDATA%\JarvisCodexNative\state.json`.

La conexión con Codex usa UTF-8 sin marcador BOM, reintenta automáticamente si
el proceso no inicia y repara respuestas antiguas mal interpretadas como
`menÃº` o `aquÃ­`.

## Arquitectura

```text
micrófono seleccionado -> Vosk (nombre) -> Whisper Small (español) -> C# nativo
                                                            |-> acción de Windows
                                                            \-> Codex Terra (solo lectura)
respuesta -> voz nativa Microsoft Raul de Windows
```

La ventana, el micrófono, las acciones, la bandeja, la lectura de voz y la
conexión con Codex son nativas en C#; Python y CMD no procesan las órdenes.

`ACTIVAR_INICIO_AUTOMATICO.bat` es opcional. Crea un acceso directo al `.exe`
en el inicio de Windows y puede revertirse con
`DESACTIVAR_INICIO_AUTOMATICO.bat`.

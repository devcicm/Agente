# Agente LLM – Modo Engine

Este repositorio contiene dos piezas:

- **Plataforma/C#/Agent.csproj**: implementación principal del agente CLI (incompleta / en desarrollo). No se recomienda compilar ni usarla por ahora.
- **Plataforma/C#/engine/**: proyecto de consola estable que permite comunicarse con el LLM (sin operaciones de sistema como archivos, bash o base de datos).

## Uso recomendado (engine)

1. Ir a la carpeta del engine:
   ```powershell
   cd Plataforma\C#\engine
   ```
2. Modo interactivo (por defecto sin streaming):
   ```powershell
   dotnet run
   ```
   - Comandos dentro del prompt:
     - `/stream on|off` para activar/desactivar SSE.
     - `/debug on|off` para ver logs y JSON raw.
     - `/test` para probar conectividad básica.
     - `/exit` para salir.
3. Modo single-shot:
   ```powershell
   dotnet run -- "tu mensaje"
   ```
   Con SSE:
   ```powershell
   dotnet run -- --stream "tu mensaje"
   ```
4. Configurar endpoint/modelo (opcional):
   - `LMSTUDIO_URL` (por defecto `http://localhost:1234`)
   - `LMSTUDIO_MODEL` (por defecto `gpt-oss-20b-gpt-5-reasoning-distill`)

## Advertencia

- **No compilar ni usar `Plataforma/C#/Agent.csproj`**: está incompleto y en fase de desarrollo.
- El engine en `Plataforma/C#/engine` es la vía soportada para comunicarse con el LLM sin operaciones de sistema.

## Notas

- El engine soporta SSE: muestra los chunks en vivo y un contador de tiempo; si no se recibe contenido, lo indica explícitamente.
- En modo no streaming, si la respuesta llega vacía, se muestra un aviso.

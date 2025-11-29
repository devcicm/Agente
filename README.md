# Agente LLM - Modo Engine

Este proyecto usa LM Studio como servidor de LLM local. La parte estable es el engine de consola; el agente principal en `Plataforma/C#/Agent.csproj` esta incompleto y no debe usarse por ahora.

## Que es LM Studio
LM Studio es una aplicacion de escritorio para descargar y ejecutar modelos LLM de forma local. Expone una API compatible con OpenAI (chat completions) y una API propia (`/v1/responses`) que podemos consumir desde el engine.

- Descarga: https://lmstudio.ai/
- Tras instalar, abre LM Studio y descarga un modelo (ej. `gpt-oss-20b-gpt-5-reasoning-distill`).
- En la pestaña de servidor, habilita el endpoint local (por defecto `http://localhost:1234`). Si quieres acceder desde otra maquina, permite conexiones LAN y ajusta el puerto si es necesario.

## Uso recomendado (solo engine)
1) Ir a la carpeta del engine:
```
cd Plataforma\C#\engine
```
2) Modo interactivo (por defecto sin streaming):
```
dotnet run
```
Comandos dentro del prompt:
- `/stream on|off` activa/desactiva SSE (streaming).
- `/debug on|off` muestra logs y JSON raw.
- `/test` prueba conectividad basica.
- `/exit` sale del programa.

3) Modo single-shot:
```
dotnet run -- "tu mensaje"
```
Con SSE:
```
dotnet run -- --stream "tu mensaje"
```

4) Configurar endpoint/modelo (opcional):
- `LMSTUDIO_URL` (default `http://localhost:1234`)
- `LMSTUDIO_MODEL` (default `gpt-oss-20b-gpt-5-reasoning-distill`)

## Advertencias
- No compiles ni uses `Plataforma/C#/Agent.csproj`: esta incompleto y en fase de desarrollo.
- Usa solo `Plataforma/C#/engine` para comunicarte con el LLM; no expone operaciones de sistema (archivos, bash, bases de datos).
- En streaming el engine muestra un contador; si no llegan datos del LLM, lo reporta. En modo no streaming avisa si la respuesta llega vacia.

## Estado del repositorio
- Engine funcional en `Plataforma/C#/engine`.
- Agente principal en `Plataforma/C#/Agent.csproj` pendiente de completar; no recomendado.

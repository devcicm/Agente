# Ejemplos de Uso - Agente en Consola de Sistema

## EjecuciИn como Programa Nativo

### Windows (cmd.exe)

```batch
@echo off
REM Ejemplo de script batch para ejecutar el agente
SET LMSTUDIO_URL=http://localhost:1234
SET LMSTUDIO_MODEL=gpt-oss-20b-gpt-5-reasoning-distill

agente-npm.exe "隅Cuケl es la capital de Francia?"

REM Para modo interactivo:
agente-npm.exe
```

### Linux/Mac (bash)

```bash
#!/bin/bash
# Ejemplo de script bash para ejecutar el agente
export LMSTUDIO_URL=http://localhost:1234
export LMSTUDIO_MODEL=gpt-oss-20b-gpt-5-reasoning-distill

./agente-npm "隅Cuケl es la capital de Francia?"

# Para modo interactivo:
./agente-npm
```

## Ejemplos de Commands

### Probar conexiИn

```bash
agente-npm test
```

### Listar modelos disponibles

```bash
agente-npm models
```

### Cambiar modelo

```bash
agente-npm model nuevo-modelo-id
```

### Activar modo streaming

```bash
agente-npm stream
```

### Enviar prompt directamente

```bash
agente-npm "Explica el concepto de inteligencia artificial"
```

## IntegraciИn con Scripts

### Ejemplo de integraciИn con script de procesamiento

```batch
@echo off
SETLOCAL

REM Configurar entorno
SET LMSTUDIO_URL=http://localhost:1234
SET LMSTUDIO_MODEL=gpt-oss-20b-gpt-5-reasoning-distill

REM Procesar mカltiples preguntas
ECHO Procesando preguntas...
agente-npm "隅Cuケl es la capital de Francia?" > respuesta1.txt
agente-npm "隅Cuケl es la capital de Espaヵa?" > respuesta2.txt
agente-npm "隅Cuケl es la capital de Alemania?" > respuesta3.txt

ECHO Procesamiento completado.
ENDLOCAL
```

### Ejemplo de integraciИn con pipeline

```bash
# Usar el agente en un pipeline
echo "隅Cuケl es la capital de Francia?" | agente-npm | grep "Parヴs"
```

## ConfiguraciИn Avanzada

### Usar variables de entorno

```bash
# Configurar variables de entorno antes de ejecutar
export LMSTUDIO_URL=http://localhost:1234
export LMSTUDIO_MODEL=gpt-oss-20b-gpt-5-reasoning-distill
export DEBUG_MODE=true
export STREAM_MODE=true

./agente-npm "Tu pregunta aquヴ"
```

### Ejecutar en segundo plano

```bash
# Ejecutar en segundo plano y guardar salida
./agente-npm "Pregunta larga" > salida.txt 2>&1 &
```

## Modo Interactivo

### Commands disponibles en modo interactivo

```
/exit        - Salir del programa
/clear       - Limpiar pantalla
/models      - Listar modelos disponibles
/model <id>  - Cambiar modelo activo
/stream      - Activar modo streaming
/nostream    - Desactivar modo streaming
/debug       - Activar modo debug
/nodebug     - Desactivar modo debug
/test        - Probar conexiИn con LM Studio
```

### Ejemplo de sesiИn interactiva

```
==== Agente CLI - Modo Consola de Sistema ====
Modelo: gpt-oss-20b-gpt-5-reasoning-distill
Endpoint: http://localhost:1234
Modo: Batch
Debug: Desactivado

Comandos disponibles:
  /exit        - Salir
  /clear       - Limpiar pantalla
  /models      - Listar modelos
  /model <id>  - Cambiar modelo
  /stream      - Activar streaming
  /nostream    - Desactivar streaming
  /debug       - Activar debug
  /nodebug     - Desactivar debug
  /test        - Probar conexiИn

> 隅Cuケl es la capital de Francia?

Prompt: 隅Cuケl es la capital de Francia?

=== Respuesta ===
La capital de Francia es Parヴs.
=================

> /exit

雁Hasta luego!
```

## Notas Importantes

1. **Compatibilidad**: El agente estケ diseヵado para funcionar en consolas de sistema estケndar
2. **Rendimiento**: Optimizado para ejecuciИn en entornos de producciИn
3. **Portabilidad**: Los ejecutables compilados pueden ejecutarse sin Node.js instalado
4. **IntegraciИn**: Puede integrarse fケcilmente con scripts y pipelines existentes


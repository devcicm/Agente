@echo off
REM Script batch para ejecutar el agente NPM en Windows
REM Este script configura el entorno y ejecuta el agente

SETLOCAL

REM Cambiar al directorio raiz del proyecto (un nivel arriba de scripts)
pushd "%~dp0\.."

REM Configurar variables de entorno
SET LMSTUDIO_URL=http://localhost:1234
SET LMSTUDIO_MODEL=gpt-oss-20b-gpt-5-reasoning-distill
SET DEBUG_MODE=false
SET STREAM_MODE=false

REM Verificar si Node.js esta instalado
WHERE node >nul 2>nul
IF %ERRORLEVEL% NEQ 0 (
    ECHO Error: Node.js no esta instalado.
    ECHO Por favor instale Node.js desde https://nodejs.org/
    PAUSE
    EXIT /B 1
)

REM Verificar si el agente esta compilado
IF EXIST "agente-npm.exe" (
    ECHO Ejecutando agente compilado...
    agente-npm.exe %*
) ELSE (
    IF EXIST "agente-npm-win.exe" (
        ECHO Ejecutando agente compilado...
        agente-npm-win.exe %*
    ) ELSE (
        ECHO Ejecutando agente con Node.js...
        node index.js %*
    )
)

popd
ENDLOCAL


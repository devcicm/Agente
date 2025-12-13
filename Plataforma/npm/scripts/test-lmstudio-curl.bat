@echo off
REM Script para probar conexión con LM Studio usando curl
REM Este script verifica si LM Studio está ejecutándose y lista los modelos disponibles

SETLOCAL

REM Cambiar al directorio raíz del proyecto (un nivel arriba de scripts)
pushd "%~dp0\.."

REM Configuración
SET LMSTUDIO_URL=http://localhost:1234
SET OUTPUT_FILE=lmstudio_response.json

ECHO ============================================
ECHO  Probando conexión con LM Studio
ECHO ============================================
ECHO.

REM Verificar si curl está disponible
WHERE curl >nul 2>nul
IF %ERRORLEVEL% NEQ 0 (
    ECHO ❌ Error: curl no está instalado.
    ECHO Por favor instale curl o use el agente NPM.
    PAUSE
    EXIT /B 1
)

ECHO 🌐 Probando endpoint de health...
curl -s -o health_response.txt -w "%%{http_code}" "%LMSTUDIO_URL%/health"
SET HEALTH_STATUS=%ERRORLEVEL%

IF "%HEALTH_STATUS%" == "200" (
    ECHO ✅ Conexión exitosa con LM Studio
    ECHO Estado: %HEALTH_STATUS%
) ELSE (
    ECHO ❌ No se pudo conectar a LM Studio
    ECHO Estado: %HEALTH_STATUS%
    ECHO.
    ECHO Verifique que:
    ECHO 1. LM Studio esté instalado y ejecutándose
    ECHO 2. El servidor esté en %LMSTUDIO_URL%
    ECHO 3. El puerto 1234 esté abierto
    PAUSE
    EXIT /B 1
)

ECHO.
ECHO 📋 Obteniendo modelos disponibles...
curl -s -o %OUTPUT_FILE% -w "%%{http_code}" "%LMSTUDIO_URL%/v1/models"
SET MODELS_STATUS=%ERRORLEVEL%

IF "%MODELS_STATUS%" == "200" (
    ECHO ✅ Modelos obtenidos exitosamente
    ECHO.
    ECHO 📄 Contenido de la respuesta:
    TYPE %OUTPUT_FILE%
) ELSE (
    ECHO ❌ Error obteniendo modelos
    ECHO Estado: %MODELS_STATUS%
    IF EXIST %OUTPUT_FILE% (
        ECHO.
        ECHO 📄 Contenido de la respuesta (posiblemente error):
        TYPE %OUTPUT_FILE%
    )
)

ECHO.
ECHO ============================================
ECHO  Prueba completada
ECHO ============================================

popd
ENDLOCAL

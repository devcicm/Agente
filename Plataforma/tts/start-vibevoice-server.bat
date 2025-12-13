@echo off
REM Script para iniciar el servidor VibeVoice TTS

echo ====================================
echo  Iniciando Servidor VibeVoice TTS
echo ====================================
echo.

REM Verificar Python
python --version >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Python no esta instalado o no esta en PATH
    echo.
    echo Instala Python 3.9+ desde: https://www.python.org/downloads/
    pause
    exit /b 1
)

REM Verificar que VibeVoice este clonado
if not exist "..\repo\VibeVoice" (
    echo [ERROR] VibeVoice no encontrado en ..\repo\VibeVoice
    echo.
    echo Clona el repositorio con:
    echo   cd ..\repo
    echo   git clone https://github.com/microsoft/VibeVoice.git
    pause
    exit /b 1
)

echo [OK] Python encontrado
echo [OK] VibeVoice encontrado
echo.

REM Variables de configuracion
set MODEL_PATH=microsoft/VibeVoice-Realtime-0.5B
set PORT=3000
set DEVICE=cuda

REM Permitir override por variables de entorno
if defined VIBEVOICE_MODEL set MODEL_PATH=%VIBEVOICE_MODEL%
if defined VIBEVOICE_PORT set PORT=%VIBEVOICE_PORT%
if defined VIBEVOICE_DEVICE set DEVICE=%VIBEVOICE_DEVICE%

echo Configuracion:
echo   - Modelo: %MODEL_PATH%
echo   - Puerto: %PORT%
echo   - Device: %DEVICE%
echo.

echo Iniciando servidor...
echo URL: http://localhost:%PORT%
echo WebSocket: ws://localhost:%PORT%/stream
echo.
echo Presiona Ctrl+C para detener el servidor
echo ====================================
echo.

REM Cambiar al directorio de VibeVoice
cd ..\repo\VibeVoice

REM Instalar dependencias si es necesario
if not exist "..\..\..\venv\" (
    echo [INFO] Primera ejecucion - Instalando dependencias...
    echo Esto puede tomar varios minutos...
    echo.
    pip install -e . >nul 2>&1
    if errorlevel 1 (
        echo [WARN] Instalacion con warnings - continuando...
    ) else (
        echo [OK] Dependencias instaladas
    )
    echo.
)

REM Iniciar servidor
python demo\vibevoice_realtime_demo.py --model_path %MODEL_PATH% --port %PORT% --device %DEVICE%

REM Si el servidor falla
if errorlevel 1 (
    echo.
    echo ====================================
    echo [ERROR] El servidor fallo al iniciar
    echo ====================================
    echo.
    echo Posibles causas:
    echo   1. GPU CUDA no disponible (prueba con --device cpu)
    echo   2. Modelo no descargado (se descarga automaticamente en primera ejecucion)
    echo   3. Dependencias faltantes (ejecuta: pip install -e .)
    echo   4. Puerto %PORT% ya en uso
    echo.
    pause
    exit /b 1
)

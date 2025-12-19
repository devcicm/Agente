@echo off
REM ============================================================================
REM Script para iniciar el servidor VibeVoice TTS (Windows)
REM Usa run-vibevoice-server.py con pyshim para compatibilidad torch.xpu
REM ============================================================================

setlocal enabledelayedexpansion

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

echo [OK] Python encontrado
python --version
echo.

REM Verificar que VibeVoice este clonado
if not exist "..\..\repo\VibeVoice" (
    echo [ERROR] VibeVoice no encontrado en ..\..\repo\VibeVoice
    echo.
    echo Clonando VibeVoice automaticamente...

    if not exist "..\..\repo" mkdir "..\..\repo"
    cd ..\..\repo

    git --version >nul 2>&1
    if errorlevel 1 (
        echo [ERROR] Git no esta instalado. Clona manualmente:
        echo   cd ..\..\repo
        echo   git clone https://github.com/microsoft/VibeVoice.git
        pause
        exit /b 1
    )

    echo Clonando desde GitHub...
    git clone https://github.com/microsoft/VibeVoice.git

    if errorlevel 1 (
        echo [ERROR] Fallo al clonar VibeVoice
        pause
        exit /b 1
    )

    echo [OK] VibeVoice clonado exitosamente
    cd ..\Plataforma\tts
) else (
    echo [OK] VibeVoice encontrado
)
echo.

REM Variables de configuracion (con defaults)
if not defined VIBEVOICE_MODEL set VIBEVOICE_MODEL=microsoft/VibeVoice-Realtime-0.5B
if not defined VIBEVOICE_PORT set VIBEVOICE_PORT=3000
if not defined VIBEVOICE_DEVICE set VIBEVOICE_DEVICE=cpu

echo Configuracion:
echo   - Modelo: %VIBEVOICE_MODEL%
echo   - Puerto: %VIBEVOICE_PORT%
echo   - Device: %VIBEVOICE_DEVICE%
echo.

REM Cambiar al directorio demo de VibeVoice
cd ..\..\repo\VibeVoice\demo

REM Instalar dependencias en primera ejecucion
if not exist "..\..\..\..\..\venv\" (
    echo [INFO] Primera ejecucion - Instalando dependencias VibeVoice...
    echo Esto puede tomar 5-10 minutos...
    echo.

    cd ..
    pip install -e . 2>&1 | findstr /V "WARNING"

    if errorlevel 1 (
        echo [WARN] Algunas advertencias durante instalacion - continuando...
    ) else (
        echo [OK] Dependencias instaladas correctamente
    )
    echo.
    cd demo
)

REM Verificar dependencias del servidor
echo [INFO] Verificando dependencias del servidor...
pip show fastapi >nul 2>&1
if errorlevel 1 (
    echo [INFO] Instalando fastapi y uvicorn...
    pip install fastapi "uvicorn[standard]" websockets
)
echo.

REM Configurar PYTHONPATH para incluir el shim de compatibilidad
set PYTHONPATH=%CD%\..\..\..\Plataforma\tts\pyshim;%PYTHONPATH%
echo [OK] PYTHONPATH configurado con pyshim para compatibilidad torch.xpu
echo.

echo Iniciando servidor...
echo   URL: http://localhost:%VIBEVOICE_PORT%
echo   WebSocket: ws://localhost:%VIBEVOICE_PORT%/stream
echo   Health: http://localhost:%VIBEVOICE_PORT%/config
echo.
echo Presiona Ctrl+C para detener el servidor
echo ====================================
echo.

REM Iniciar servidor usando el script Python mejorado
python ..\..\..\Plataforma\tts\run-vibevoice-server.py

REM Manejar errores
if errorlevel 1 (
    echo.
    echo ====================================
    echo [ERROR] El servidor fallo al iniciar
    echo ====================================
    echo.
    echo Posibles causas:
    echo   1. Puerto %VIBEVOICE_PORT% ya en uso
    echo      Solucion: set VIBEVOICE_PORT=3001
    echo.
    echo   2. GPU CUDA no disponible
    echo      Solucion: set VIBEVOICE_DEVICE=cpu
    echo.
    echo   3. Modelo no descargado
    echo      Se descarga automaticamente - requiere internet
    echo.
    echo   4. Dependencias faltantes
    echo      Ejecuta: pip install -e . (en VibeVoice/)
    echo.
    echo   5. torch.xpu incompatibilidad
    echo      El pyshim deberia solucionarlo automaticamente
    echo.
    pause
    exit /b 1
)

endlocal

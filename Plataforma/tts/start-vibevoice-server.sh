#!/bin/bash

# Script para iniciar el servidor VibeVoice TTS (Linux/Mac)

echo "===================================="
echo " Iniciando Servidor VibeVoice TTS"
echo "===================================="
echo ""

# Verificar Python
if ! command -v python3 &> /dev/null; then
    echo "[ERROR] Python 3 no está instalado"
    echo ""
    echo "Instala Python 3.9+ desde tu gestor de paquetes"
    exit 1
fi

# Verificar que VibeVoice esté clonado
if [ ! -d "../repo/VibeVoice" ]; then
    echo "[ERROR] VibeVoice no encontrado en ../repo/VibeVoice"
    echo ""
    echo "Clona el repositorio con:"
    echo "  cd ../repo"
    echo "  git clone https://github.com/microsoft/VibeVoice.git"
    exit 1
fi

echo "[OK] Python encontrado"
echo "[OK] VibeVoice encontrado"
echo ""

# Variables de configuración
MODEL_PATH="${VIBEVOICE_MODEL:-microsoft/VibeVoice-Realtime-0.5B}"
PORT="${VIBEVOICE_PORT:-3000}"
DEVICE="${VIBEVOICE_DEVICE:-cuda}"

echo "Configuración:"
echo "  - Modelo: $MODEL_PATH"
echo "  - Puerto: $PORT"
echo "  - Device: $DEVICE"
echo ""

echo "Iniciando servidor..."
echo "URL: http://localhost:$PORT"
echo "WebSocket: ws://localhost:$PORT/stream"
echo ""
echo "Presiona Ctrl+C para detener el servidor"
echo "===================================="
echo ""

# Cambiar al directorio de VibeVoice
cd ../repo/VibeVoice || exit 1

# Instalar dependencias si es necesario
if [ ! -d "../../../venv" ]; then
    echo "[INFO] Primera ejecución - Instalando dependencias..."
    echo "Esto puede tomar varios minutos..."
    echo ""
    pip3 install -e . > /dev/null 2>&1
    if [ $? -ne 0 ]; then
        echo "[WARN] Instalación con warnings - continuando..."
    else
        echo "[OK] Dependencias instaladas"
    fi
    echo ""
fi

# Iniciar servidor
python3 demo/vibevoice_realtime_demo.py \
    --model_path "$MODEL_PATH" \
    --port "$PORT" \
    --device "$DEVICE"

# Si el servidor falla
if [ $? -ne 0 ]; then
    echo ""
    echo "===================================="
    echo "[ERROR] El servidor falló al iniciar"
    echo "===================================="
    echo ""
    echo "Posibles causas:"
    echo "  1. GPU CUDA no disponible (prueba con --device cpu o mps para Mac)"
    echo "  2. Modelo no descargado (se descarga automáticamente en primera ejecución)"
    echo "  3. Dependencias faltantes (ejecuta: pip install -e .)"
    echo "  4. Puerto $PORT ya en uso"
    echo ""
    exit 1
fi

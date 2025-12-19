#!/bin/bash
# ============================================================================
# Script para iniciar el servidor VibeVoice TTS (Linux/Mac)
# Usa run-vibevoice-server.py con pyshim para compatibilidad torch.xpu
# ============================================================================

set -e  # Exit on error

echo "===================================="
echo " Iniciando Servidor VibeVoice TTS"
echo "===================================="
echo ""

# Verificar Python
if ! command -v python3 &> /dev/null; then
    echo "[ERROR] Python 3 no está instalado"
    echo ""
    echo "Instala Python 3.9+ desde tu gestor de paquetes:"
    echo "  Ubuntu/Debian: sudo apt install python3 python3-pip"
    echo "  macOS: brew install python3"
    echo "  Fedora: sudo dnf install python3 python3-pip"
    exit 1
fi

echo "[OK] Python encontrado"
python3 --version
echo ""

# Verificar que VibeVoice esté clonado
if [ ! -d "../../repo/VibeVoice" ]; then
    echo "[ERROR] VibeVoice no encontrado en ../../repo/VibeVoice"
    echo ""
    echo "Clonando VibeVoice automáticamente..."

    mkdir -p "../../repo"
    cd ../../repo

    if ! command -v git &> /dev/null; then
        echo "[ERROR] Git no está instalado. Clona manualmente:"
        echo "  cd ../../repo"
        echo "  git clone https://github.com/microsoft/VibeVoice.git"
        exit 1
    fi

    echo "Clonando desde GitHub..."
    git clone https://github.com/microsoft/VibeVoice.git

    if [ $? -ne 0 ]; then
        echo "[ERROR] Falló al clonar VibeVoice"
        exit 1
    fi

    echo "[OK] VibeVoice clonado exitosamente"
    cd Plataforma/tts
else
    echo "[OK] VibeVoice encontrado"
fi
echo ""

# Variables de configuración (con defaults)
export VIBEVOICE_MODEL="${VIBEVOICE_MODEL:-microsoft/VibeVoice-Realtime-0.5B}"
export VIBEVOICE_PORT="${VIBEVOICE_PORT:-3000}"

# Auto-detectar device (MPS para Mac M1+, CPU para otros)
if [ -z "$VIBEVOICE_DEVICE" ]; then
    if [[ "$OSTYPE" == "darwin"* ]] && python3 -c "import torch; print(torch.backends.mps.is_available())" 2>/dev/null | grep -q "True"; then
        export VIBEVOICE_DEVICE="mps"
        echo "[INFO] Mac M1+ detectado - usando MPS (Metal Performance Shaders)"
    else
        export VIBEVOICE_DEVICE="cpu"
        echo "[INFO] Usando CPU (más lento pero compatible)"
    fi
fi

echo "Configuración:"
echo "  - Modelo: $VIBEVOICE_MODEL"
echo "  - Puerto: $VIBEVOICE_PORT"
echo "  - Device: $VIBEVOICE_DEVICE"
echo ""

# Cambiar al directorio demo de VibeVoice
cd ../../repo/VibeVoice/demo || exit 1

# Instalar dependencias en primera ejecución
if [ ! -d "../../../../venv" ]; then
    echo "[INFO] Primera ejecución - Instalando dependencias VibeVoice..."
    echo "Esto puede tomar 5-10 minutos..."
    echo ""

    cd ..
    pip3 install -e . 2>&1 | grep -v "WARNING" || true

    if [ $? -ne 0 ]; then
        echo "[WARN] Algunas advertencias durante instalación - continuando..."
    else
        echo "[OK] Dependencias instaladas correctamente"
    fi
    echo ""
    cd demo
fi

# Verificar dependencias del servidor
echo "[INFO] Verificando dependencias del servidor..."
if ! pip3 show fastapi &> /dev/null; then
    echo "[INFO] Instalando fastapi y uvicorn..."
    pip3 install fastapi "uvicorn[standard]" websockets
fi
echo ""

# Configurar PYTHONPATH para incluir el shim de compatibilidad
# SCRIPT_DIR apunta a Plataforma/tts/
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export PYTHONPATH="$SCRIPT_DIR/pyshim:$PYTHONPATH"
echo "[OK] PYTHONPATH configurado con pyshim para compatibilidad torch.xpu"
echo ""

echo "Iniciando servidor..."
echo "  URL: http://localhost:$VIBEVOICE_PORT"
echo "  WebSocket: ws://localhost:$VIBEVOICE_PORT/stream"
echo "  Health: http://localhost:$VIBEVOICE_PORT/config"
echo ""
echo "Presiona Ctrl+C para detener el servidor"
echo "===================================="
echo ""

# Iniciar servidor usando el script Python mejorado
# Estamos en repo/VibeVoice/demo, SCRIPT_DIR es Plataforma/tts
python3 "$SCRIPT_DIR/run-vibevoice-server.py"

# Manejar errores
if [ $? -ne 0 ]; then
    echo ""
    echo "===================================="
    echo "[ERROR] El servidor falló al iniciar"
    echo "===================================="
    echo ""
    echo "Posibles causas:"
    echo "  1. Puerto $VIBEVOICE_PORT ya en uso"
    echo "     Solución: export VIBEVOICE_PORT=3001"
    echo ""
    echo "  2. GPU/MPS no disponible"
    echo "     Solución: export VIBEVOICE_DEVICE=cpu"
    echo ""
    echo "  3. Modelo no descargado"
    echo "     Se descarga automáticamente - requiere internet"
    echo ""
    echo "  4. Dependencias faltantes"
    echo "     Ejecuta: pip3 install -e . (en VibeVoice/)"
    echo ""
    echo "  5. torch.xpu incompatibilidad"
    echo "     El pyshim debería solucionarlo automáticamente"
    echo ""
    exit 1
fi

#!/bin/bash
# Script bash para ejecutar el agente NPM en Linux/Mac
# Este script configura el entorno y ejecuta el agente

# Cambiar al directorio raiz del proyecto (un nivel arriba de scripts)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.." || exit 1

# Configurar variables de entorno
export LMSTUDIO_URL=http://localhost:1234
export LMSTUDIO_MODEL=gpt-oss-20b-gpt-5-reasoning-distill
export DEBUG_MODE=false
export STREAM_MODE=false

# Verificar si Node.js esta instalado
if ! command -v node &> /dev/null; then
    echo "Error: Node.js no esta instalado."
    echo "Por favor instale Node.js desde https://nodejs.org/"
    exit 1
fi

# Verificar si el agente esta compilado
if [ -f "agente-npm" ]; then
    echo "Ejecutando agente compilado..."
    ./agente-npm "$@"
elif [ -f "agente-npm-linux" ]; then
    echo "Ejecutando agente compilado..."
    ./agente-npm-linux "$@"
elif [ -f "agente-npm-macos" ]; then
    echo "Ejecutando agente compilado..."
    ./agente-npm-macos "$@"
else
    echo "Ejecutando agente con Node.js..."
    node index.js "$@"
fi


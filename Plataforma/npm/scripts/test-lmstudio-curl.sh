#!/bin/bash
# Script para probar conexiÇün con LM Studio usando curl
# Este script verifica si LM Studio estÇ­ ejecutÇ­ndose y lista los modelos disponibles

# Cambiar al directorio raíz del proyecto (un nivel arriba de scripts)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.." || exit 1

echo "============================================"
echo "  Probando conexiÇün con LM Studio"
echo "============================================"
echo ""

# ConfiguraciÇün
LMSTUDIO_URL="http://localhost:1234"
OUTPUT_FILE="lmstudio_response.json"

# Verificar si curl estÇ­ disponible
if ! command -v curl &> /dev/null; then
    echo "ƒ?O Error: curl no estÇ­ instalado."
    echo "Por favor instale curl o use el agente NPM."
    exit 1
fi

echo "ÐYO? Probando endpoint de health..."
HEALTH_STATUS=$(curl -s -o health_response.txt -w "%{http_code}" "$LMSTUDIO_URL/health")

if [ "$HEALTH_STATUS" -eq 200 ]; then
    echo "ƒo. ConexiÇün exitosa con LM Studio"
    echo "Estado: $HEALTH_STATUS"
else
    echo "ƒ?O No se pudo conectar a LM Studio"
    echo "Estado: $HEALTH_STATUS"
    echo ""
    echo "Verifique que:"
    echo "1. LM Studio estÇ¸ instalado y ejecutÇ­ndose"
    echo "2. El servidor estÇ¸ en $LMSTUDIO_URL"
    echo "3. El puerto 1234 estÇ¸ abierto"
    exit 1
fi

echo ""
echo "ÐY"< Obteniendo modelos disponibles..."
MODELS_STATUS=$(curl -s -o $OUTPUT_FILE -w "%{http_code}" "$LMSTUDIO_URL/v1/models")

if [ "$MODELS_STATUS" -eq 200 ]; then
    echo "ƒo. Modelos obtenidos exitosamente"
    echo ""
    echo "ÐY"" Contenido de la respuesta:"
    cat $OUTPUT_FILE
else
    echo "ƒ?O Error obteniendo modelos"
    echo "Estado: $MODELS_STATUS"
    if [ -f "$OUTPUT_FILE" ]; then
        echo ""
        echo "ÐY"" Contenido de la respuesta (posiblemente error):"
        cat $OUTPUT_FILE
    fi
fi

echo ""
echo "============================================"
echo "  Prueba completada"
echo "============================================"


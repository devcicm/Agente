#!/usr/bin/env python3
"""
VibeVoice TTS Server Launcher
==============================

Lanzador mejorado para el servidor VibeVoice con:
- Shim de compatibilidad torch.xpu para CPU-only PyTorch
- Validación de dependencias
- Mejor manejo de errores
- Logging estructurado
- Auto-configuración de paths

Uso:
    python run-vibevoice-server.py

Variables de entorno:
    VIBEVOICE_MODEL   - Modelo a usar (default: microsoft/VibeVoice-Realtime-0.5B)
    VIBEVOICE_PORT    - Puerto del servidor (default: 3000)
    VIBEVOICE_DEVICE  - Dispositivo: cuda, cpu, mps (default: cpu)
"""

import os
import sys
import types
import logging
from pathlib import Path

# =============================================================================
# Configurar logging (compatible con Windows CP1252)
# =============================================================================
# Configurar stdout para UTF-8 en Windows
if sys.platform == "win32":
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace')

logging.basicConfig(
    level=logging.INFO,
    format="[%(levelname)s] %(message)s",
    handlers=[logging.StreamHandler(sys.stdout)]
)
logger = logging.getLogger(__name__)

# =============================================================================
# Torch XPU Compatibility Shim
# =============================================================================
logger.info("Aplicando shim de compatibilidad torch.xpu...")
try:
    import torch

    if not hasattr(torch, "xpu"):
        # Crear namespace dummy para torch.xpu
        torch.xpu = types.SimpleNamespace(
            empty_cache=lambda: None,
            is_available=lambda: False,
            device_count=lambda: 0,
            manual_seed=lambda seed: torch.manual_seed(seed),
            reset_peak_memory_stats=lambda *args, **kwargs: None,
            max_memory_allocated=lambda *args, **kwargs: 0,
            synchronize=lambda *args, **kwargs: None,
        )
        logger.info("[OK] torch.xpu shim aplicado correctamente")
    else:
        logger.info("[OK] torch.xpu ya está disponible")
except ImportError as e:
    logger.error(f"[ERROR] Error al importar torch: {e}")
    logger.error("Instala PyTorch con: pip install torch")
    sys.exit(1)

# =============================================================================
# Configuración del servidor
# =============================================================================
model = os.environ.get("VIBEVOICE_MODEL", "microsoft/VibeVoice-Realtime-0.5B")
port = int(os.environ.get("VIBEVOICE_PORT", "3000"))
device = os.environ.get("VIBEVOICE_DEVICE", "cpu")

logger.info("=" * 60)
logger.info("Configuración del servidor VibeVoice:")
logger.info(f"  Modelo:  {model}")
logger.info(f"  Puerto:  {port}")
logger.info(f"  Device:  {device}")
logger.info("=" * 60)

# Validar device
valid_devices = ["cuda", "cpu", "mps"]
if device not in valid_devices:
    logger.warning(f"Device '{device}' no reconocido, usando 'cpu'")
    device = "cpu"

# Verificar disponibilidad de CUDA/MPS
if device == "cuda" and not torch.cuda.is_available():
    logger.warning("CUDA no disponible, cambiando a CPU")
    device = "cpu"
elif device == "mps" and not (hasattr(torch.backends, "mps") and torch.backends.mps.is_available()):
    logger.warning("MPS no disponible, cambiando a CPU")
    device = "cpu"

# Configurar variables de entorno para VibeVoice
os.environ["MODEL_PATH"] = model
os.environ["MODEL_DEVICE"] = device

# =============================================================================
# Validar estructura de directorios
# =============================================================================
logger.info("Validando estructura de directorios...")

# Debemos estar en VibeVoice/demo/
cwd = Path.cwd()
logger.info(f"Directorio actual: {cwd}")

# Verificar que existe web/app.py
app_module_path = cwd / "web" / "app.py"
if not app_module_path.exists():
    logger.error(f"[ERROR] No se encontró web/app.py en {app_module_path}")
    logger.error("Este script debe ejecutarse desde el directorio VibeVoice/demo/")
    sys.exit(1)

logger.info(f"[OK] Módulo web.app encontrado en {app_module_path}")

# Agregar directorio actual al path para importar web.app
sys.path.insert(0, str(cwd))

# =============================================================================
# Verificar dependencias
# =============================================================================
logger.info("Verificando dependencias...")

required_packages = {
    "fastapi": "fastapi",
    "uvicorn": "uvicorn",
    "websockets": "websockets",
}

missing_packages = []
for package_name, import_name in required_packages.items():
    try:
        __import__(import_name)
        logger.info(f"[OK] {package_name}")
    except ImportError:
        logger.warning(f"[MISSING] {package_name} no está instalado")
        missing_packages.append(package_name)

if missing_packages:
    logger.error("Instala las dependencias faltantes con:")
    logger.error(f"  pip install {' '.join(missing_packages)}")
    sys.exit(1)

# =============================================================================
# Iniciar servidor
# =============================================================================
logger.info("=" * 60)
logger.info("Iniciando servidor Uvicorn...")
logger.info(f"  URL:       http://0.0.0.0:{port}")
logger.info(f"  WebSocket: ws://0.0.0.0:{port}/stream")
logger.info(f"  Health:    http://0.0.0.0:{port}/config")
logger.info("=" * 60)
logger.info("Presiona Ctrl+C para detener el servidor")
logger.info("")

try:
    import uvicorn

    # Configuración de uvicorn
    uvicorn_config = {
        "app": "web.app:app",
        "host": "0.0.0.0",
        "port": port,
        "reload": False,
        "log_level": "info",
        "access_log": True,
    }

    uvicorn.run(**uvicorn_config)

except KeyboardInterrupt:
    logger.info("")
    logger.info("=" * 60)
    logger.info("Servidor detenido por el usuario")
    logger.info("=" * 60)
    sys.exit(0)

except OSError as e:
    if "Address already in use" in str(e) or "Only one usage" in str(e):
        logger.error("=" * 60)
        logger.error(f"[ERROR] Puerto {port} ya está en uso")
        logger.error("=" * 60)
        logger.error("")
        logger.error("Soluciones:")
        logger.error(f"  1. Usa otro puerto: export VIBEVOICE_PORT=3001")
        logger.error(f"  2. Detén el proceso usando el puerto {port}")
        logger.error("")
        sys.exit(1)
    else:
        logger.error(f"Error del sistema operativo: {e}")
        sys.exit(1)

except Exception as e:
    logger.error("=" * 60)
    logger.error(f"[ERROR] Error inesperado al iniciar el servidor")
    logger.error("=" * 60)
    logger.error(f"Tipo: {type(e).__name__}")
    logger.error(f"Mensaje: {e}")
    logger.error("")
    logger.error("Stacktrace completo:")
    import traceback
    traceback.print_exc()
    sys.exit(1)

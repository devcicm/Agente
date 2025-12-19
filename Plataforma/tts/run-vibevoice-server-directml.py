#!/usr/bin/env python3
"""
VibeVoice TTS Server con soporte DirectML Multi-GPU
====================================================

Soporta:
- DirectML (AMD, Intel, NVIDIA en Windows)
- CUDA (solo NVIDIA)
- CPU (fallback)
- Selección manual de GPU específica

Uso:
    # Auto-detectar mejor GPU
    python run-vibevoice-server-directml.py

    # Usar GPU específica
    DIRECTML_DEVICE=0 python run-vibevoice-server-directml.py  # GPU integrada
    DIRECTML_DEVICE=1 python run-vibevoice-server-directml.py  # GPU dedicada

Variables de entorno:
    VIBEVOICE_MODEL   - Modelo a usar (default: microsoft/VibeVoice-Realtime-0.5B)
    VIBEVOICE_PORT    - Puerto del servidor (default: 3000)
    VIBEVOICE_DEVICE  - Dispositivo: directml, cuda, cpu (default: auto)
    DIRECTML_DEVICE   - Índice de GPU para DirectML (0, 1, etc.)
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

# Suprimir warnings de APEX y transformers
class ApexWarningFilter(logging.Filter):
    def filter(self, record):
        msg = record.getMessage()
        # Filtrar warnings comunes que no afectan funcionalidad
        suppress_keywords = [
            "APEX FusedRMSNorm",
            "OPTIMIZE_FOR_SPEED",
            "Using the `WANDB_DISABLED` environment variable",
        ]
        return not any(kw in msg for kw in suppress_keywords)

logging.getLogger("transformers").addFilter(ApexWarningFilter())
logging.getLogger("transformers.modeling_utils").addFilter(ApexWarningFilter())

# =============================================================================
# Detección y configuración de dispositivo GPU
# =============================================================================

def detect_and_select_device():
    """Detectar automáticamente el mejor dispositivo disponible"""

    device_type = os.environ.get("VIBEVOICE_DEVICE", "auto").lower()
    gpu_index = os.environ.get("DIRECTML_DEVICE", None)

    logger.info("=" * 60)
    logger.info("Detectando dispositivos disponibles...")
    logger.info("=" * 60)

    # Si el usuario especificó el tipo de device
    if device_type in ["cuda", "directml", "cpu"]:
        logger.info(f"Dispositivo especificado: {device_type}")

        if device_type == "cuda":
            return setup_cuda()
        elif device_type == "directml":
            return setup_directml(gpu_index)
        else:
            return setup_cpu()

    # Auto-detección
    logger.info("Modo auto-detección activado")

    # 1. Intentar CUDA (mejor rendimiento)
    try:
        import torch
        if torch.cuda.is_available():
            logger.info("✓ CUDA detectado (GPU NVIDIA)")
            return setup_cuda()
    except:
        pass

    # 2. Intentar DirectML (funciona con AMD, Intel, NVIDIA)
    try:
        import torch_directml
        if torch_directml.device_count() > 0:
            logger.info("✓ DirectML detectado")
            return setup_directml(gpu_index)
    except ImportError:
        logger.info("✗ DirectML no disponible (instala: pip install torch-directml)")
    except:
        pass

    # 3. Fallback a CPU
    logger.info("✓ Usando CPU (sin aceleración GPU)")
    return setup_cpu()

def setup_cuda():
    """Configurar dispositivo CUDA"""
    import torch

    if not torch.cuda.is_available():
        logger.warning("CUDA solicitado pero no disponible, usando CPU")
        return setup_cpu()

    device = torch.device("cuda")
    gpu_name = torch.cuda.get_device_name(0)

    logger.info("=" * 60)
    logger.info("Configuración CUDA:")
    logger.info(f"  GPU: {gpu_name}")
    logger.info(f"  VRAM: {torch.cuda.get_device_properties(0).total_memory / 1e9:.2f} GB")
    logger.info(f"  Device: {device}")
    logger.info("=" * 60)

    # Shim torch.xpu para compatibilidad
    if not hasattr(torch, "xpu"):
        torch.xpu = types.SimpleNamespace(
            empty_cache=lambda: None,
            is_available=lambda: False,
            device_count=lambda: 0,
            manual_seed=lambda seed: torch.manual_seed(seed),
            reset_peak_memory_stats=lambda *args, **kwargs: None,
            max_memory_allocated=lambda *args, **kwargs: 0,
            synchronize=lambda *args, **kwargs: None,
        )

    return device, "cuda"

def setup_directml(gpu_index=None):
    """Configurar dispositivo DirectML con selección de GPU"""
    try:
        import torch_directml
        import torch
    except ImportError:
        logger.error("DirectML solicitado pero torch-directml no está instalado")
        logger.error("Instala con: pip install torch-directml")
        return setup_cpu()

    # Detectar GPUs disponibles
    device_count = torch_directml.device_count()

    if device_count == 0:
        logger.warning("DirectML instalado pero no se detectaron GPUs")
        return setup_cpu()

    # Seleccionar GPU
    if gpu_index is not None:
        try:
            selected_gpu = int(gpu_index)
            if selected_gpu >= device_count:
                logger.warning(f"GPU {selected_gpu} no existe, usando GPU 0")
                selected_gpu = 0
        except ValueError:
            logger.warning(f"Índice de GPU inválido: {gpu_index}, usando GPU 0")
            selected_gpu = 0
    else:
        # Si hay múltiples GPUs, preferir la dedicada (usualmente índice 1)
        # Heurística simple: GPU con índice mayor suele ser dedicada
        selected_gpu = device_count - 1 if device_count > 1 else 0

    device = torch_directml.device(selected_gpu)

    # Intentar obtener nombre de GPU
    try:
        if hasattr(torch_directml, 'device_name'):
            gpu_name = torch_directml.device_name(selected_gpu)
        else:
            gpu_name = f"DirectML Device {selected_gpu}"
    except:
        gpu_name = f"DirectML Device {selected_gpu}"

    logger.info("=" * 60)
    logger.info("Configuración DirectML:")
    logger.info(f"  GPUs disponibles: {device_count}")
    logger.info(f"  GPU seleccionada: {selected_gpu}")
    logger.info(f"  Nombre: {gpu_name}")
    logger.info(f"  Device: {device}")

    if device_count > 1:
        logger.info("")
        logger.info("  Tienes múltiples GPUs. Para cambiar, usa:")
        logger.info("    $env:DIRECTML_DEVICE = \"0\"  # GPU integrada")
        logger.info("    $env:DIRECTML_DEVICE = \"1\"  # GPU dedicada")

    logger.info("=" * 60)

    # Shim torch.xpu para compatibilidad
    if not hasattr(torch, "xpu"):
        torch.xpu = types.SimpleNamespace(
            empty_cache=lambda: None,
            is_available=lambda: False,
            device_count=lambda: 0,
            manual_seed=lambda seed: torch.manual_seed(seed),
            reset_peak_memory_stats=lambda *args, **kwargs: None,
            max_memory_allocated=lambda *args, **kwargs: 0,
            synchronize=lambda *args, **kwargs: None,
        )

    return device, f"directml:{selected_gpu}"

def setup_cpu():
    """Configurar dispositivo CPU"""
    import torch

    # Optimizar para CPU
    torch.set_num_threads(os.cpu_count())

    device = torch.device("cpu")

    logger.info("=" * 60)
    logger.info("Configuración CPU:")
    logger.info(f"  Threads: {torch.get_num_threads()}")
    logger.info(f"  Device: {device}")
    logger.info("=" * 60)

    # Shim torch.xpu para compatibilidad
    if not hasattr(torch, "xpu"):
        torch.xpu = types.SimpleNamespace(
            empty_cache=lambda: None,
            is_available=lambda: False,
            device_count=lambda: 0,
            manual_seed=lambda seed: torch.manual_seed(seed),
            reset_peak_memory_stats=lambda *args, **kwargs: None,
            max_memory_allocated=lambda *args, **kwargs: 0,
            synchronize=lambda *args, **kwargs: None,
        )

    return device, "cpu"

# =============================================================================
# Configuración del servidor
# =============================================================================

# Detectar y configurar dispositivo
device, device_name = detect_and_select_device()

model = os.environ.get("VIBEVOICE_MODEL", "microsoft/VibeVoice-Realtime-0.5B")
port = int(os.environ.get("VIBEVOICE_PORT", "3000"))

logger.info("")
logger.info("=" * 60)
logger.info("Configuración del servidor VibeVoice:")
logger.info(f"  Modelo:  {model}")
logger.info(f"  Puerto:  {port}")
logger.info(f"  Device:  {device_name}")
logger.info("=" * 60)
logger.info("")

# Configurar variables de entorno para VibeVoice
os.environ["MODEL_PATH"] = model
os.environ["MODEL_DEVICE"] = str(device)

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
        logger.error(f"  1. Usa otro puerto: $env:VIBEVOICE_PORT = \"3001\"")
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

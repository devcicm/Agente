# Optimización de Rendimiento VibeVoice con GPU AMD Radeon RX 640

## Tu Hardware Detectado

- **GPU Principal:** AMD Radeon RX 640 (4 GB VRAM)
- **GPU Integrada:** Intel UHD Graphics 750 (2 GB)
- **PyTorch Actual:** 2.2.1+cpu (solo CPU)

## Situación Actual

❌ **APEX NO ESTÁ DISPONIBLE** porque:
- APEX requiere GPU NVIDIA con CUDA
- Tu GPU AMD no soporta CUDA
- APEX FusedRMSNorm solo funciona con CUDA

## Opciones de Aceleración para tu Hardware

### Opción 1: PyTorch con DirectML (Recomendado para AMD en Windows)

DirectML es la tecnología de Microsoft que permite aceleración GPU con AMD/Intel en Windows.

**Instalación:**

```bash
# Desinstalar PyTorch CPU
pip uninstall torch torchvision torchaudio

# Instalar PyTorch-DirectML (soporte AMD GPU en Windows)
pip install torch-directml
```

**Configurar VibeVoice para DirectML:**

Modificar `run-vibevoice-server.py`:

```python
# Cambiar la sección de device detection (línea ~90)
# De:
if device == "cuda" and not torch.cuda.is_available():
    logger.warning("CUDA no disponible, cambiando a CPU")
    device = "cpu"

# A:
if device == "cuda" and not torch.cuda.is_available():
    # Intentar DirectML como fallback
    try:
        import torch_directml
        device = torch_directml.device()
        logger.info("Usando DirectML para aceleración GPU AMD/Intel")
    except ImportError:
        logger.warning("DirectML no disponible, cambiando a CPU")
        device = "cpu"
```

**Ganancia esperada:** 1.5-2x más rápido vs CPU

**Limitaciones:**
- DirectML es más lento que CUDA
- Menos optimizado que soluciones NVIDIA
- Algunos modelos pueden no ser compatibles

---

### Opción 2: ROCm (Complicado en Windows)

ROCm es el equivalente de AMD a CUDA, pero el soporte en Windows es limitado y experimental.

**NO RECOMENDADO** porque:
- ROCm oficialmente solo soporta Linux
- RX 640 puede no estar en la lista de GPUs soportadas
- Requiere WSL2 (Windows Subsystem for Linux)
- Configuración muy compleja

---

### Opción 3: Optimizaciones en CPU (Más Práctico)

Ya que no puedes usar APEX, optimiza el rendimiento en CPU:

**1. Habilitar threading optimizado:**

```python
# Agregar al inicio de run-vibevoice-server.py (después de imports)
import torch
torch.set_num_threads(os.cpu_count())  # Usar todos los cores de CPU
torch.set_num_interop_threads(2)
```

**2. Compilación JIT (Just-In-Time):**

```python
# Si el modelo lo soporta
torch.jit.enable_onednn_fusion(True)
```

**3. Usar formato de modelo optimizado:**

```bash
# Instalar optimizador Intel para CPU
pip install intel-extension-for-pytorch
```

**Ganancia esperada:** 20-40% mejora vs CPU no optimizado

---

### Opción 4: Suprimir Warning de APEX (Estético)

Si solo quieres eliminar el warning sin perder funcionalidad:

**Crear archivo:** `Plataforma/tts/suppress_apex_warning.py`

```python
"""Filtro para suprimir warning de APEX FusedRMSNorm"""
import logging

class ApexWarningFilter(logging.Filter):
    def filter(self, record):
        # Suprimir warnings sobre APEX
        if "APEX FusedRMSNorm" in record.getMessage():
            return False
        if "OPTIMIZE_FOR_SPEED" in record.getMessage():
            return False
        return True

def suppress_apex_warnings():
    """Aplicar filtro a loggers de transformers y vibevoice"""
    logging.getLogger("transformers").addFilter(ApexWarningFilter())
    logging.getLogger("transformers.modeling_utils").addFilter(ApexWarningFilter())
```

**Modificar `run-vibevoice-server.py`:**

```python
# Después de configurar logging (línea ~42)
from suppress_apex_warning import suppress_apex_warnings
suppress_apex_warnings()
```

---

## Implementación Automática: Script con DirectML

Crear archivo: `Plataforma/tts/run-vibevoice-directml.py`

```python
#!/usr/bin/env python3
"""
VibeVoice con soporte DirectML para AMD/Intel GPUs
"""
import os
import sys

# Intentar usar DirectML primero
try:
    import torch_directml
    DEFAULT_DEVICE = torch_directml.device()
    print("[INFO] DirectML detectado - usando aceleración GPU AMD/Intel")
except ImportError:
    import torch
    DEFAULT_DEVICE = "cpu"
    print("[INFO] DirectML no disponible - usando CPU")

# Configurar device por defecto
os.environ.setdefault("VIBEVOICE_DEVICE", str(DEFAULT_DEVICE))

# Importar y ejecutar servidor normal
exec(open("run-vibevoice-server.py").read())
```

**Uso:**
```powershell
# Instalar DirectML
pip install torch-directml

# Ejecutar servidor con DirectML
python run-vibevoice-directml.py
```

---

## Comparación de Rendimiento (Estimado)

| Método | Velocidad Relativa | Complejidad | Recomendado |
|--------|-------------------|-------------|-------------|
| **CPU actual** | 1.0x (baseline) | Muy fácil | ✅ Ya funciona |
| **CPU optimizado** | 1.2-1.4x | Fácil | ✅ Sí |
| **DirectML** | 1.5-2.0x | Media | ✅ Sí, si funciona |
| **ROCm** | 2.0-3.0x | Muy difícil | ❌ No en Windows |
| **CUDA/APEX** | 3.0-5.0x | N/A | ❌ Requiere GPU NVIDIA |

---

## Recomendación Final

**Para tu hardware (AMD RX 640):**

1. **Primero:** Intentar DirectML → mejora moderada, configuración media
2. **Alternativa:** Optimizar CPU → mejora pequeña, muy fácil
3. **Estético:** Suprimir warning → sin cambio de rendimiento

**Si quieres máximo rendimiento:**
- Considera actualizar a GPU NVIDIA (RTX 3060 o superior)
- O acepta que el rendimiento en AMD será limitado para este tipo de modelos

## ¿Qué deseas hacer?

A) Instalar y configurar DirectML (mejor opción para tu hardware)
B) Solo optimizar CPU (más simple)
C) Suprimir el warning (sin cambio de rendimiento)
D) Ninguna, aceptar el warning y continuar

Responde con A, B, C o D y te ayudo a implementarlo.

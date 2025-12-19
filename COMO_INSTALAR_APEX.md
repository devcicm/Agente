# Cómo Instalar APEX para Obtener Máximo Rendimiento en VibeVoice

## Estado Actual

- **PyTorch:** 2.2.1+cpu (versión CPU solamente)
- **CUDA:** No instalado
- **APEX:** No disponible (requiere CUDA)

## ¿Por qué no funciona APEX?

APEX FusedRMSNorm es una optimización **exclusiva para GPU NVIDIA con CUDA**. La versión CPU de PyTorch no puede usar APEX porque:

1. APEX usa kernels CUDA optimizados que solo funcionan en GPU
2. La implementación fusionada requiere operaciones de GPU
3. No existe equivalente CPU de FusedRMSNorm en APEX

## Opciones para Obtener Mejor Rendimiento

### Opción 1: Instalar CUDA + PyTorch GPU + APEX (Mejor Rendimiento)

**Requisitos:**
- GPU NVIDIA (GTX 1060 o superior recomendado)
- Windows 10/11
- ~10 GB espacio en disco para CUDA Toolkit

**Pasos:**

1. **Verificar que tienes GPU NVIDIA:**
   ```powershell
   # En PowerShell
   Get-WmiObject Win32_VideoController | Select-Object Name, AdapterRAM
   ```

2. **Instalar CUDA Toolkit 11.8 o 12.1:**
   - Descargar desde: https://developer.nvidia.com/cuda-downloads
   - Seleccionar: Windows -> x86_64 -> Version de Windows -> exe (local)
   - Instalar con opciones por defecto

3. **Desinstalar PyTorch CPU e instalar PyTorch con CUDA:**
   ```bash
   # Desinstalar versión CPU
   pip uninstall torch torchvision torchaudio

   # Instalar versión CUDA 11.8
   pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118

   # O para CUDA 12.1
   pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu121
   ```

4. **Verificar instalación de PyTorch con CUDA:**
   ```bash
   python -c "import torch; print('CUDA disponible:', torch.cuda.is_available())"
   ```

5. **Instalar Visual Studio Build Tools:**
   - Descargar: https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022
   - Instalar "Desktop development with C++"

6. **Instalar APEX:**
   ```bash
   # Clonar repositorio APEX
   git clone https://github.com/NVIDIA/apex
   cd apex

   # Instalar con extensiones CUDA
   pip install -v --disable-pip-version-check --no-cache-dir --no-build-isolation --config-settings "--build-option=--cpp_ext" --config-settings "--build-option=--cuda_ext" ./
   ```

7. **Configurar VibeVoice para usar APEX:**
   ```powershell
   # En el script de inicio
   $env:OPTIMIZE_FOR_SPEED = "1"
   .\start-vibevoice-server.ps1 -Device cuda
   ```

**Ganancia de rendimiento esperada:** 2-5x más rápido vs CPU

---

### Opción 2: Solo PyTorch CUDA sin APEX (Buen Rendimiento)

Si no puedes instalar APEX (problemas de compilación), aún obtendrás gran mejora con GPU:

```bash
# Instalar PyTorch con CUDA
pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118

# Iniciar servidor con CUDA
.\start-vibevoice-server.ps1 -Device cuda
```

**Ganancia de rendimiento esperada:** 1.5-3x más rápido vs CPU

---

### Opción 3: Optimizaciones en CPU (Rendimiento Actual)

Si no tienes GPU NVIDIA, puedes optimizar el rendimiento en CPU:

1. **Usar quantización (reducir precisión):**
   ```python
   # Agregar en run-vibevoice-server.py antes de cargar el modelo
   torch.set_num_threads(os.cpu_count())  # Usar todos los cores
   ```

2. **Instalar PyTorch optimizado para CPU:**
   ```bash
   # Versión con Intel MKL (más rápido en CPU Intel)
   pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cpu
   ```

3. **Reducir batch size para menor latencia**

**Ganancia de rendimiento esperada:** 10-30% mejora vs configuración actual

---

## Recomendación

1. **Si tienes GPU NVIDIA:** Sigue Opción 1 (CUDA + APEX)
2. **Si tienes GPU NVIDIA pero APEX falla:** Sigue Opción 2 (solo CUDA)
3. **Si solo tienes CPU:** Sigue Opción 3 (optimizaciones CPU)

## Verificar GPU Disponible

```powershell
# PowerShell
nvidia-smi  # Si esto funciona, tienes GPU NVIDIA
```

## Solución de Problemas

### Error: "CUDA out of memory"
- Reducir batch size
- Usar modelo más pequeño
- Cerrar otras aplicaciones que usen GPU

### Error: "No CUDA-capable device is detected"
- Verificar que drivers NVIDIA estén actualizados
- Reiniciar después de instalar CUDA Toolkit

### Error al compilar APEX
- Verificar que Visual Studio Build Tools esté instalado
- Asegurarse que CUDA Toolkit coincida con versión PyTorch
- Intentar instalar versión pre-compilada (si existe para tu configuración)

## Contacto

Para más ayuda, consulta:
- APEX: https://github.com/NVIDIA/apex
- PyTorch CUDA: https://pytorch.org/get-started/locally/

# VibeVoice TTS con DirectML Multi-GPU

Sistema flexible para ejecutar VibeVoice TTS con soporte para **cualquier marca de GPU** en Windows usando DirectML.

## Tu Hardware

Detección automática de tu sistema:
- **GPU Dedicada:** AMD Radeon RX 640 (4 GB VRAM)
- **GPU Integrada:** Intel UHD Graphics 750 (2 GB VRAM)
- **DirectML:** ✅ Instalado y funcionando
- **GPUs disponibles:** 2

## Características

✅ **Multi-GPU**: Elige entre GPU integrada o dedicada
✅ **Multi-marca**: Funciona con AMD, Intel, NVIDIA
✅ **Auto-detección**: Selecciona automáticamente el mejor dispositivo
✅ **Flexible**: Cambia de GPU sin reinstalar nada
✅ **Sin vendor lock-in**: No limitado a NVIDIA

## Instalación Rápida

```powershell
# Ya está instalado! DirectML está listo para usar
```

## Uso Básico

### 1. Auto-detectar mejor GPU
```powershell
.\start-vibevoice-server-directml.ps1
```

### 2. Listar GPUs disponibles
```powershell
.\start-vibevoice-server-directml.ps1 -ListGpus
```

### 3. Usar GPU Dedicada (AMD Radeon RX 640) - RECOMENDADO
```powershell
.\start-vibevoice-server-directml.ps1 -Device directml -GpuIndex 0
```

### 4. Usar GPU Integrada (Intel UHD 750)
```powershell
.\start-vibevoice-server-directml.ps1 -Device directml -GpuIndex 1
```

### 5. Usar solo CPU
```powershell
.\start-vibevoice-server-directml.ps1 -Device cpu
```

## Comparación de Rendimiento Estimado

| Dispositivo | Velocidad Estimada | Uso Energía | Recomendado |
|-------------|-------------------|-------------|-------------|
| **AMD RX 640** | ~2.0x | Alto | ✅ Máximo rendimiento |
| **Intel UHD 750** | ~1.5x | Bajo | ⚡ Ahorro energía |
| **CPU** | 1.0x (baseline) | Medio | 🐌 Solo si falla GPU |

## Benchmark de Rendimiento

Ejecuta este comando para comparar el rendimiento real de tus GPUs:

```powershell
python Plataforma/tts/test-directml-performance.py
```

El script medirá y comparará automáticamente:
- CPU
- GPU 0 (AMD Radeon RX 640)
- GPU 1 (Intel UHD Graphics 750)

## Ejemplos Avanzados

### Puerto personalizado
```powershell
.\start-vibevoice-server-directml.ps1 -Device directml -GpuIndex 0 -Port 3001
```

### Con variables de entorno
```powershell
$env:VIBEVOICE_DEVICE = "directml"
$env:DIRECTML_DEVICE = "0"  # 0=AMD, 1=Intel
.\start-vibevoice-server-directml.ps1
```

### Modelo personalizado
```powershell
.\start-vibevoice-server-directml.ps1 -Model "microsoft/VibeVoice-Realtime-0.5B" -Device directml -GpuIndex 0
```

## Estructura de Archivos

```
Plataforma/tts/
├── start-vibevoice-server-directml.ps1   # Script principal con multi-GPU
├── run-vibevoice-server-directml.py      # Servidor Python con DirectML
├── detect-gpus.py                        # Detector de GPUs
├── test-directml-performance.py          # Benchmark de rendimiento
└── README_DIRECTML.md                    # Este archivo
```

## Comandos Útiles

### Ver información de GPUs
```powershell
python Plataforma/tts/detect-gpus.py
```

### Test rápido de DirectML
```powershell
python Plataforma/tts/detect-gpus.py --test
```

### Verificar instalación
```powershell
python -c "import torch_directml; print('DirectML OK:', torch_directml.device_count(), 'GPUs')"
```

## Troubleshooting

### "DirectML no está instalado"
```powershell
pip uninstall torch torchvision torchaudio
pip install torch-directml
```

### "Puerto ya en uso"
```powershell
# Usar otro puerto
.\start-vibevoice-server-directml.ps1 -Port 3001
```

### "GPU no funciona"
1. Verifica drivers actualizados
2. Confirma que DirectX 12 está instalado (viene con Windows 10/11)
3. Intenta con la otra GPU
4. Fallback a CPU

### Warning de APEX FusedRMSNorm
✅ **Normal** - Este warning es inofensivo. APEX es solo para NVIDIA CUDA, DirectML usa su propia optimización.

## Comparación: DirectML vs CUDA

| Característica | DirectML | CUDA |
|---------------|----------|------|
| **Marcas soportadas** | AMD, Intel, NVIDIA | Solo NVIDIA |
| **Sistema operativo** | Solo Windows | Windows, Linux |
| **Rendimiento** | Bueno (60-70% de CUDA) | Excelente (100%) |
| **Facilidad instalación** | Muy fácil | Media |
| **Tu hardware** | ✅ Funciona | ❌ No disponible |

## Rendimiento Esperado (VibeVoice TTS)

### Generar 10 segundos de audio:

| Configuración | Tiempo Estimado | Latencia |
|--------------|----------------|----------|
| CPU (Intel) | ~5.0 segundos | Alta |
| Intel UHD 750 | ~3.5 segundos | Media |
| AMD RX 640 | ~2.5 segundos | Baja |
| NVIDIA RTX 3060 (CUDA) | ~1.5 segundos | Muy baja |

## FAQ

### ¿Cuál GPU debo usar?
**AMD Radeon RX 640** (GPU 0) - Mejor rendimiento

### ¿Funciona con otros modelos?
Sí, DirectML funciona con cualquier modelo PyTorch

### ¿Puedo usar CUDA?
No, necesitas GPU NVIDIA. Con AMD solo DirectML o CPU.

### ¿DirectML consume más batería?
Sí, usar GPU consume más que CPU. Para ahorrar batería usa Intel UHD 750 o CPU.

### ¿Funciona en Linux?
No, DirectML es exclusivo de Windows. En Linux con AMD usa ROCm (más complicado).

## Próximos Pasos

1. **Ejecutar benchmark**:
   ```powershell
   python Plataforma/tts/test-directml-performance.py
   ```

2. **Iniciar servidor con mejor GPU**:
   ```powershell
   .\start-vibevoice-server-directml.ps1 -Device directml -GpuIndex 0
   ```

3. **Probar el servidor**:
   ```powershell
   # En otro terminal
   curl http://localhost:3000/config
   ```

## Soporte

Para problemas o preguntas:
- Ejecuta: `python Plataforma/tts/detect-gpus.py` y reporta el output
- Incluye el error completo
- Especifica qué GPU intentaste usar

## Licencia

Mismo que VibeVoice (Microsoft)

---

**Creado:** 2025-12-19
**Versión DirectML:** 0.2.5.dev240914
**PyTorch:** 2.4.1

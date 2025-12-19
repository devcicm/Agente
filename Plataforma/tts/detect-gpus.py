#!/usr/bin/env python3
"""
Detector de GPUs disponibles para DirectML
Muestra todas las GPUs y permite seleccionar cuál usar
"""

import sys
import platform

def detect_gpus_windows():
    """Detectar GPUs en Windows usando WMI"""
    try:
        import subprocess
        result = subprocess.run(
            ['powershell', '-Command',
             'Get-WmiObject Win32_VideoController | Select-Object Name, AdapterRAM, DriverVersion | ConvertTo-Json'],
            capture_output=True,
            text=True,
            encoding='utf-8'
        )

        if result.returncode == 0:
            import json
            gpus = json.loads(result.stdout)
            if not isinstance(gpus, list):
                gpus = [gpus]
            return gpus
        else:
            return []
    except Exception as e:
        print(f"Error detectando GPUs con WMI: {e}")
        return []

def detect_directml_devices():
    """Detectar dispositivos DirectML disponibles"""
    try:
        import torch_directml
        device_count = torch_directml.device_count()
        devices = []

        for i in range(device_count):
            device = torch_directml.device(i)
            devices.append({
                'index': i,
                'device': device,
                'name': torch_directml.device_name(i) if hasattr(torch_directml, 'device_name') else f'DirectML Device {i}'
            })

        return devices
    except ImportError:
        print("\n[ERROR] torch-directml no está instalado")
        print("Instala con: pip install torch-directml")
        return None
    except Exception as e:
        print(f"\n[ERROR] Error detectando dispositivos DirectML: {e}")
        return []

def format_ram(ram_bytes):
    """Formatear RAM en GB"""
    if ram_bytes is None or ram_bytes == 0:
        return "N/A"
    gb = ram_bytes / (1024**3)
    return f"{gb:.2f} GB"

def main():
    print("=" * 70)
    print("DETECTOR DE GPUs PARA DIRECTML")
    print("=" * 70)
    print()

    # Detectar sistema operativo
    os_name = platform.system()
    print(f"Sistema Operativo: {os_name}")
    print()

    if os_name != "Windows":
        print("[ADVERTENCIA] DirectML solo funciona en Windows")
        print("Tu sistema no es compatible")
        return

    # Detectar GPUs del sistema
    print("-" * 70)
    print("GPUs DETECTADAS EN EL SISTEMA:")
    print("-" * 70)

    gpus = detect_gpus_windows()
    if gpus:
        for i, gpu in enumerate(gpus):
            print(f"\n[{i}] {gpu.get('Name', 'GPU Desconocida')}")
            print(f"    VRAM:   {format_ram(gpu.get('AdapterRAM'))}")
            print(f"    Driver: {gpu.get('DriverVersion', 'N/A')}")
    else:
        print("[ERROR] No se pudieron detectar GPUs")

    print()
    print("-" * 70)
    print("DISPOSITIVOS DIRECTML DISPONIBLES:")
    print("-" * 70)

    # Detectar dispositivos DirectML
    directml_devices = detect_directml_devices()

    if directml_devices is None:
        print("\nPara usar DirectML, instala primero:")
        print("  pip uninstall torch torchvision torchaudio")
        print("  pip install torch-directml")
        return

    if not directml_devices:
        print("\n[ERROR] No se encontraron dispositivos DirectML")
        print("Verifica que:")
        print("  1. Tienes drivers de GPU actualizados")
        print("  2. DirectX 12 está instalado (viene con Windows 10/11)")
        return

    print()
    for device_info in directml_devices:
        print(f"[{device_info['index']}] {device_info['name']}")
        print(f"    Device: {device_info['device']}")

    print()
    print("=" * 70)
    print("RECOMENDACIONES:")
    print("=" * 70)
    print()

    # Identificar GPU recomendada
    if len(gpus) >= 2:
        print("Tienes múltiples GPUs:")
        print()
        for i, gpu in enumerate(gpus):
            name = gpu.get('Name', '')
            ram = gpu.get('AdapterRAM', 0)

            # Determinar tipo
            if 'Intel' in name or 'UHD' in name or 'HD Graphics' in name:
                gpu_type = "Integrada (CPU)"
                recommendation = "Bajo consumo, rendimiento moderado"
            elif 'NVIDIA' in name or 'GeForce' in name or 'RTX' in name:
                gpu_type = "Dedicada (NVIDIA)"
                recommendation = "Mejor rendimiento con CUDA"
            elif 'AMD' in name or 'Radeon' in name:
                gpu_type = "Dedicada (AMD)"
                recommendation = "Buen rendimiento con DirectML"
            else:
                gpu_type = "Desconocida"
                recommendation = "Prueba y compara"

            print(f"  [{i}] {name}")
            print(f"      Tipo: {gpu_type}")
            print(f"      VRAM: {format_ram(ram)}")
            print(f"      Recomendación: {recommendation}")
            print()

        # Recomendar GPU dedicada si existe
        dedicated_gpu = None
        for i, gpu in enumerate(gpus):
            name = gpu.get('Name', '')
            if 'AMD' in name or 'Radeon' in name or 'NVIDIA' in name or 'GeForce' in name:
                dedicated_gpu = i
                break

        if dedicated_gpu is not None:
            print(f"RECOMENDADO: Usa GPU [{dedicated_gpu}] (Dedicada)")
            print()

    print("PARA USAR UNA GPU ESPECÍFICA:")
    print()
    print("  Opción 1 - Variable de entorno:")
    print("    $env:DIRECTML_DEVICE = \"0\"    # GPU integrada")
    print("    $env:DIRECTML_DEVICE = \"1\"    # GPU dedicada")
    print()
    print("  Opción 2 - Parámetro en script:")
    print("    .\\start-vibevoice-server.ps1 -Device directml -GpuIndex 0")
    print("    .\\start-vibevoice-server.ps1 -Device directml -GpuIndex 1")
    print()
    print("=" * 70)

    # Test rápido si se solicita
    if len(sys.argv) > 1 and sys.argv[1] == "--test":
        print()
        print("EJECUTANDO TEST DE DIRECTML...")
        print()

        try:
            import torch_directml
            import torch

            for device_info in directml_devices:
                idx = device_info['index']
                device = torch_directml.device(idx)

                print(f"Testing GPU [{idx}]...")

                # Test simple
                x = torch.randn(1000, 1000)
                x_gpu = x.to(device)
                y_gpu = x_gpu @ x_gpu.T
                y = y_gpu.cpu()

                print(f"  ✓ GPU [{idx}] funciona correctamente")
                print(f"    Resultado shape: {y.shape}")

        except Exception as e:
            print(f"  ✗ Error en test: {e}")

    print()

if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""
Test de rendimiento DirectML vs CPU
Compara la velocidad de ambas GPUs y CPU
"""

import time
import torch
import sys

def benchmark_device(device, device_name, iterations=10):
    """Ejecutar benchmark en un dispositivo específico"""
    print(f"\nBenchmarking: {device_name}")
    print("-" * 50)

    # Warm-up
    x = torch.randn(1000, 1000)
    x_dev = x.to(device)
    _ = x_dev @ x_dev.T

    # Benchmark
    times = []
    for i in range(iterations):
        start = time.time()

        # Operación matricial grande
        x = torch.randn(2000, 2000)
        x_dev = x.to(device)
        y = x_dev @ x_dev.T
        result = y.cpu()

        elapsed = time.time() - start
        times.append(elapsed)

        if (i + 1) % 5 == 0:
            print(f"  Iteración {i+1}/{iterations}: {elapsed*1000:.2f} ms")

    avg_time = sum(times) / len(times)
    print(f"\n  Promedio: {avg_time*1000:.2f} ms")
    print(f"  Min: {min(times)*1000:.2f} ms")
    print(f"  Max: {max(times)*1000:.2f} ms")

    return avg_time

def main():
    print("=" * 70)
    print("TEST DE RENDIMIENTO DIRECTML")
    print("=" * 70)

    results = {}

    # Test CPU
    try:
        print("\n[1/3] Testing CPU...")
        cpu_time = benchmark_device(torch.device("cpu"), "CPU", iterations=5)
        results["CPU"] = cpu_time
    except Exception as e:
        print(f"Error en CPU: {e}")

    # Test DirectML GPUs
    try:
        import torch_directml

        device_count = torch_directml.device_count()
        print(f"\n\nGPUs DirectML detectadas: {device_count}")

        for i in range(device_count):
            try:
                device = torch_directml.device(i)
                device_name = torch_directml.device_name(i) if hasattr(torch_directml, 'device_name') else f"GPU {i}"

                print(f"\n[{i+2}/{device_count+1}] Testing {device_name}...")
                gpu_time = benchmark_device(device, device_name, iterations=10)
                results[f"GPU {i}: {device_name}"] = gpu_time
            except Exception as e:
                print(f"Error en GPU {i}: {e}")

    except ImportError:
        print("\n[ERROR] torch-directml no está instalado")
        print("Instala con: pip install torch-directml")

    # Resultados finales
    print("\n" + "=" * 70)
    print("RESULTADOS FINALES")
    print("=" * 70)

    if not results:
        print("No se pudieron ejecutar benchmarks")
        return

    # Ordenar por velocidad
    sorted_results = sorted(results.items(), key=lambda x: x[1])

    print("\nRanking (más rápido primero):")
    print("-" * 70)

    baseline = sorted_results[0][1]

    for rank, (name, time_val) in enumerate(sorted_results, 1):
        speedup = baseline / time_val if time_val > 0 else 0
        print(f"{rank}. {name:35s}  {time_val*1000:7.2f} ms  ({speedup:.2f}x)")

    print("\n" + "=" * 70)
    print("RECOMENDACIÓN:")
    print("=" * 70)

    best_device = sorted_results[0][0]
    print(f"\nUsa: {best_device} (más rápido)")

    if "GPU" in best_device:
        # Extraer índice de GPU
        if "GPU 0" in best_device:
            gpu_idx = 0
        elif "GPU 1" in best_device:
            gpu_idx = 1
        else:
            gpu_idx = 0

        print(f"\nComando:")
        print(f"  .\\start-vibevoice-server-directml.ps1 -Device directml -GpuIndex {gpu_idx}")
    else:
        print(f"\nComando:")
        print(f"  .\\start-vibevoice-server-directml.ps1 -Device cpu")

    print()

if __name__ == "__main__":
    main()

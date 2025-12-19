#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Test simple de VibeVoice TTS Server
Conecta al servidor y sintetiza una frase de prueba
"""

import asyncio
import websockets
import json
import sys
import io
from urllib.parse import urlencode

# Configurar stdout para UTF-8 en Windows
if sys.platform == "win32":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace')

async def test_tts():
    print("=" * 70)
    print("TEST DE VIBEVOICE TTS")
    print("=" * 70)
    print()

    # Configuración
    server_url = "ws://localhost:3000"
    text = "Hola, este es un test del sistema de síntesis de voz. ¿Funciona correctamente?"
    voice = "sp-Spk1_man"  # Voz masculina en español
    cfg_scale = 1.5
    steps = 2  # Optimizado para CPU

    print(f"Texto a sintetizar: {text}")
    print(f"Voz: {voice}")
    print(f"Configuración: cfg={cfg_scale}, steps={steps}")
    print()

    # Construir URL WebSocket con parámetros
    params = {
        'text': text,
        'voice': voice,
        'cfg': cfg_scale,
        'steps': steps
    }
    ws_url = f"{server_url}/stream?{urlencode(params)}"

    print(f"Conectando a: {ws_url[:80]}...")
    print()

    audio_chunks = []

    try:
        async with websockets.connect(ws_url) as websocket:
            print("✓ WebSocket conectado")
            print("Esperando audio...")
            print()

            while True:
                try:
                    message = await asyncio.wait_for(websocket.recv(), timeout=60.0)

                    # Verificar tipo de mensaje
                    if isinstance(message, bytes):
                        # Audio chunk
                        audio_chunks.append(message)
                        if len(audio_chunks) % 10 == 0:
                            print(f"  Recibidos {len(audio_chunks)} chunks de audio...")
                    else:
                        # Log message (JSON)
                        try:
                            log = json.loads(message)
                            if 'event' in log:
                                print(f"  [Evento] {log['event']}")
                        except:
                            pass

                except asyncio.TimeoutError:
                    print("✗ Timeout esperando respuesta del servidor")
                    break
                except websockets.exceptions.ConnectionClosed:
                    print("✓ Conexión cerrada por el servidor (síntesis completa)")
                    break

    except Exception as e:
        print(f"✗ Error: {e}")
        return False

    print()
    print("=" * 70)
    print("RESULTADOS:")
    print("=" * 70)
    print(f"Chunks de audio recibidos: {len(audio_chunks)}")

    if audio_chunks:
        # Concatenar audio
        total_size = sum(len(chunk) for chunk in audio_chunks)
        print(f"Tamaño total de audio: {total_size:,} bytes ({total_size/1024:.1f} KB)")

        # Calcular duración aproximada (PCM16, 24kHz, mono)
        # 24000 samples/sec * 2 bytes/sample = 48000 bytes/sec
        duration = total_size / 48000
        print(f"Duración estimada: {duration:.2f} segundos")

        # Guardar audio
        output_file = "test_tts_output.pcm"
        with open(output_file, 'wb') as f:
            for chunk in audio_chunks:
                f.write(chunk)

        print(f"✓ Audio guardado en: {output_file}")

        # Convertir a WAV
        try:
            output_wav = "test_tts_output.wav"
            wav_header = create_wav_header(total_size, sample_rate=24000)

            with open(output_wav, 'wb') as f:
                f.write(wav_header)
                for chunk in audio_chunks:
                    f.write(chunk)

            print(f"✓ Audio WAV guardado en: {output_wav}")
            print()
            print("Puedes reproducir el audio con:")
            print(f"  - Windows Media Player: {output_wav}")
            print(f"  - PowerShell: (New-Object Media.SoundPlayer '{output_wav}').PlaySync()")

        except Exception as e:
            print(f"⚠ No se pudo crear WAV: {e}")

        print()
        print("=" * 70)
        print("✓ TEST EXITOSO")
        print("=" * 70)
        return True
    else:
        print("✗ No se recibió audio")
        print("=" * 70)
        print("✗ TEST FALLIDO")
        print("=" * 70)
        return False

def create_wav_header(data_size, sample_rate=24000, num_channels=1, bits_per_sample=16):
    """Crear header WAV para audio PCM16"""
    byte_rate = sample_rate * num_channels * bits_per_sample // 8
    block_align = num_channels * bits_per_sample // 8

    header = bytearray()

    # RIFF header
    header += b'RIFF'
    header += (36 + data_size).to_bytes(4, 'little')
    header += b'WAVE'

    # fmt chunk
    header += b'fmt '
    header += (16).to_bytes(4, 'little')  # fmt chunk size
    header += (1).to_bytes(2, 'little')   # PCM format
    header += num_channels.to_bytes(2, 'little')
    header += sample_rate.to_bytes(4, 'little')
    header += byte_rate.to_bytes(4, 'little')
    header += block_align.to_bytes(2, 'little')
    header += bits_per_sample.to_bytes(2, 'little')

    # data chunk
    header += b'data'
    header += data_size.to_bytes(4, 'little')

    return bytes(header)

if __name__ == "__main__":
    result = asyncio.run(test_tts())
    exit(0 if result else 1)

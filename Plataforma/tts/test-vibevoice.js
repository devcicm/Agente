#!/usr/bin/env node
'use strict';

/**
 * Script de prueba para VibeVoice TTS Client
 * Demuestra las diferentes capacidades del cliente
 */

const VibeVoiceClient = require('./vibevoice-client');
const fs = require('fs').promises;
const path = require('path');

console.log(`
╔═══════════════════════════════════════════════════════════╗
║                                                           ║
║         Test de VibeVoice TTS Integration                ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝
`);

async function main() {
  // Crear cliente
  const client = new VibeVoiceClient({
    serverUrl: process.env.VIBEVOICE_URL || 'ws://localhost:3000',
    debug: true
  });

  console.log('📋 Configuración:');
  console.log(`   - Server: ${client.config.serverUrl}`);
  console.log(`   - Voice: ${client.config.defaultVoice}`);
  console.log(`   - CFG Scale: ${client.config.cfgScale}`);
  console.log(`   - Steps: ${client.config.steps}`);
  console.log();

  // Test 1: Verificar salud del servidor
  console.log('━'.repeat(80));
  console.log('\n[Test 1] Verificando servidor...\n');

  const isHealthy = await client.checkHealth();

  if (!isHealthy) {
    console.error('❌ Servidor no disponible');
    console.error('\nAsegúrate de iniciar el servidor primero:');
    console.error('  Windows: start-vibevoice-server.bat');
    console.error('  Linux/Mac: ./start-vibevoice-server.sh');
    console.error('\nO ejecuta:');
    console.error('  cd ../repo/VibeVoice');
    console.error('  python demo/vibevoice_realtime_demo.py --model_path microsoft/VibeVoice-Realtime-0.5B\n');
    process.exit(1);
  }

  console.log('✅ Servidor disponible\n');

  // Test 2: Listar voces
  console.log('━'.repeat(80));
  console.log('\n[Test 2] Listando voces disponibles...\n');

  const voices = await client.listVoices();
  console.log(`📢 Voces disponibles (${voices.length}):`);
  voices.forEach((voice, i) => {
    console.log(`   ${i + 1}. ${voice}`);
  });
  console.log();

  // Test 3: Síntesis simple
  console.log('━'.repeat(80));
  console.log('\n[Test 3] Síntesis simple...\n');

  const text1 = "Hello! This is a test of the VibeVoice text-to-speech system. It sounds pretty natural, doesn't it?";
  console.log(`📝 Texto: "${text1}"`);
  console.log(`🎤 Voz: Carter\n`);

  const startTime = Date.now();
  const result1 = await client.synthesize(text1, {
    voice: 'Carter',
    outputFile: 'output-test1.wav'
  });

  console.log(`\n✅ Síntesis completada:`);
  console.log(`   - Duración: ${result1.duration}ms`);
  console.log(`   - Chunks: ${result1.chunks}`);
  console.log(`   - Tamaño audio: ${(result1.audio.length / 1024).toFixed(2)} KB`);
  console.log(`   - Sample rate: ${result1.sampleRate} Hz`);
  console.log(`   - Formato: ${result1.format}`);
  console.log(`   - Archivo: output-test1.wav`);
  console.log();

  // Logs del servidor
  if (result1.logs.length > 0) {
    console.log('📊 Logs del servidor:');
    result1.logs.forEach(log => {
      console.log(`   - ${log.event}: ${JSON.stringify(log.data || {})}`);
    });
    console.log();
  }

  // Test 4: Síntesis con otra voz
  console.log('━'.repeat(80));
  console.log('\n[Test 4] Probando otra voz...\n');

  const text2 = "This is Alice speaking. The voice quality is excellent for real-time synthesis!";
  console.log(`📝 Texto: "${text2}"`);
  console.log(`🎤 Voz: Alice (si está disponible, sino default)\n`);

  const voiceToUse = voices.includes('Alice') ? 'Alice' : voices[0];
  const result2 = await client.synthesize(text2, {
    voice: voiceToUse,
    outputFile: 'output-test2.wav'
  });

  console.log(`✅ Síntesis completada con voz: ${voiceToUse}`);
  console.log(`   - Duración: ${result2.duration}ms`);
  console.log(`   - Archivo: output-test2.wav\n`);

  // Test 5: Síntesis streaming con callbacks
  console.log('━'.repeat(80));
  console.log('\n[Test 5] Síntesis con streaming (callbacks)...\n');

  const text3 = "This demonstrates real-time streaming where we process audio chunks as they arrive.";
  console.log(`📝 Texto: "${text3}"`);
  console.log(`🎤 Voz: ${client.config.defaultVoice}\n`);

  const audioChunks = [];
  let chunkCounter = 0;

  const result3 = await client.synthesizeStreaming(text3, {
    onChunk: (chunk, count) => {
      audioChunks.push(chunk);
      chunkCounter = count;
      if (count % 5 === 0) {
        process.stdout.write(`\r   📦 Chunks recibidos: ${count}`);
      }
    },
    onLog: (log) => {
      if (log.event === 'backend_first_chunk_sent') {
        console.log(`\n   ⚡ Primer chunk recibido (latencia)`);
      }
    }
  });

  console.log(`\r   📦 Chunks totales: ${chunkCounter}          `);
  console.log(`\n✅ Streaming completado:`);
  console.log(`   - Duración: ${result3.duration}ms`);
  console.log(`   - Chunks: ${result3.chunks}\n`);

  // Guardar audio del streaming
  const streamingAudio = Buffer.concat(audioChunks);
  const wavBuffer = VibeVoiceClient.pcmToWav(streamingAudio, 24000);
  await fs.writeFile('output-test3-streaming.wav', wavBuffer);
  console.log('   - Archivo: output-test3-streaming.wav\n');

  // Test 6: Texto largo
  console.log('━'.repeat(80));
  console.log('\n[Test 6] Texto largo (~200 palabras)...\n');

  const longText = `
    Text-to-speech technology has come a long way in recent years.
    Modern systems like VibeVoice use advanced artificial intelligence
    to generate natural-sounding speech that closely mimics human voice patterns.
    These systems can handle long-form content like podcasts and audiobooks,
    maintaining consistent voice quality throughout the entire duration.
    The ability to stream audio in real-time is particularly impressive,
    with initial chunks arriving in just a few hundred milliseconds.
    This makes it suitable for interactive applications where low latency
    is crucial for a good user experience. The technology continues to improve,
    with better prosody, emotion, and naturalness being added with each iteration.
  `.trim().replace(/\s+/g, ' ');

  console.log(`📝 Texto: ${longText.substring(0, 100)}...`);
  console.log(`   Longitud: ${longText.length} caracteres\n`);

  const result4 = await client.synthesize(longText, {
    voice: client.config.defaultVoice,
    outputFile: 'output-test4-long.wav'
  });

  console.log(`✅ Texto largo sintetizado:`);
  console.log(`   - Duración: ${result4.duration}ms`);
  console.log(`   - Tamaño: ${(result4.audio.length / 1024).toFixed(2)} KB`);
  console.log(`   - Archivo: output-test4-long.wav\n`);

  // Resumen final
  console.log('═'.repeat(80));
  console.log('\n📊 RESUMEN DE TESTS\n');
  console.log('═'.repeat(80));
  console.log();
  console.log('✅ Todos los tests completados exitosamente!');
  console.log();
  console.log('Archivos generados:');
  console.log('  1. output-test1.wav - Síntesis simple');
  console.log('  2. output-test2.wav - Otra voz');
  console.log('  3. output-test3-streaming.wav - Streaming');
  console.log('  4. output-test4-long.wav - Texto largo');
  console.log();
  console.log('Puedes reproducir estos archivos con cualquier reproductor de audio.');
  console.log();
  console.log('═'.repeat(80));
  console.log();
}

// Manejo de errores
process.on('unhandledRejection', (error) => {
  console.error('\n❌ Error no capturado:', error.message);
  console.error(error.stack);
  process.exit(1);
});

// Ejecutar
main().catch(error => {
  console.error('\n❌ Error:', error.message);
  process.exit(1);
});

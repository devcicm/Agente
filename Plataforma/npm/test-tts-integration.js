#!/usr/bin/env node
'use strict';

/**
 * Test de integración TTS para agente NPM
 * Verifica que el cliente VibeVoice funciona correctamente
 */

const VibeVoiceClient = require('./src/tts/vibevoice-client');

console.log('═══════════════════════════════════════════════════');
console.log('  Test de Integración TTS - Agente NPM');
console.log('═══════════════════════════════════════════════════\n');

async function testTTS() {
  try {
    // Crear cliente
    const client = new VibeVoiceClient({
      serverUrl: 'ws://localhost:3000',
      defaultVoice: 'en-Carter_man',
      debug: true
    });

    console.log('1. Verificando servidor VibeVoice...');
    const isHealthy = await client.checkHealth();

    if (!isHealthy) {
      console.error('❌ Servidor TTS no disponible en ws://localhost:3000');
      console.error('\nInicia el servidor con:');
      console.error('  cd ../tts');
      console.error('  ./start-vibevoice-server.bat\n');
      process.exit(1);
    }

    console.log('✓ Servidor disponible\n');

    // Listar voces
    console.log('2. Obteniendo voces disponibles...');
    const voices = await client.listVoices();
    console.log(`✓ ${voices.length} voces disponibles`);
    console.log(`   Voces: ${voices.slice(0, 5).join(', ')}...\n`);

    // Síntesis de prueba
    console.log('3. Sintetizando texto de prueba...');
    const testText = 'Hello! This is a test of the text-to-speech integration.';
    console.log(`   Texto: "${testText}"`);
    console.log(`   Voz: en-Carter_man\n`);

    const startTime = Date.now();
    const result = await client.synthesize(testText, {
      voice: 'en-Carter_man',
      outputFile: 'test-tts-integration.wav'
    });

    const duration = Date.now() - startTime;

    console.log('✓ Síntesis completada:');
    console.log(`   - Duración total: ${duration}ms`);
    console.log(`   - Chunks recibidos: ${result.chunks}`);
    console.log(`   - Tamaño audio: ${(result.audio.length / 1024).toFixed(2)} KB`);
    console.log(`   - Sample rate: ${result.sampleRate} Hz`);
    console.log(`   - Formato: ${result.format}`);
    console.log(`   - Archivo: test-tts-integration.wav\n`);

    console.log('═══════════════════════════════════════════════════');
    console.log('✅ TEST EXITOSO - TTS funcionando correctamente');
    console.log('═══════════════════════════════════════════════════\n');

    console.log('El archivo test-tts-integration.wav fue generado.');
    console.log('Puedes reproducirlo con cualquier reproductor de audio.\n');

    process.exit(0);

  } catch (error) {
    console.error('\n❌ ERROR:', error.message);
    console.error(error.stack);
    process.exit(1);
  }
}

// Ejecutar test
testTTS();

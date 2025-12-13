#!/usr/bin/env node
'use strict';

/**
 * Script de prueba para el Agente Autónomo
 * Demuestra diferentes capacidades del agente
 */

const { AutonomousAgent } = require('./src/agent/agent-autonomous');

console.log(`
╔═══════════════════════════════════════════════════════════╗
║                                                           ║
║         Test del Agente Autónomo con Herramientas        ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝
`);

// Crear agente
const agent = new AutonomousAgent({
  baseUrl: process.env.LMSTUDIO_URL || 'http://localhost:1234',
  model: process.env.LMSTUDIO_MODEL || 'deepseek/deepseek-r1-0528-qwen3-8b',
  maxIterations: 5,
  debug: false
});

// Tests a ejecutar
const tests = [
  {
    name: 'Test 1: Crear archivo simple',
    task: 'Crea un archivo llamado test-output.txt con el contenido "Hello from autonomous agent!"',
    description: 'Prueba la herramienta WriteFile'
  },
  {
    name: 'Test 2: Leer y analizar',
    task: 'Lee el archivo package.json y dime cuál es la versión actual',
    description: 'Prueba la herramienta ReadFile'
  },
  {
    name: 'Test 3: Comando de sistema',
    task: 'Ejecuta el comando "node --version" y dime qué versión de Node.js está instalada',
    description: 'Prueba la herramienta Bash'
  },
  {
    name: 'Test 4: Búsqueda de archivos',
    task: 'Busca todos los archivos .js en el directorio src/ que contengan la palabra "agent"',
    description: 'Prueba la herramienta Grep'
  },
  {
    name: 'Test 5: HTTP Request',
    task: 'Haz un request GET a https://api.github.com/zen y muéstrame la respuesta',
    description: 'Prueba la herramienta Curl'
  }
];

// Función para ejecutar tests
async function runTests() {
  const results = [];

  for (let i = 0; i < tests.length; i++) {
    const test = tests[i];

    console.log(`\n${'━'.repeat(80)}`);
    console.log(`\n${test.name}`);
    console.log(`📝 ${test.description}`);
    console.log(`🎯 Tarea: "${test.task}"`);
    console.log();

    try {
      const startTime = Date.now();
      const result = await agent.executeTask(test.task);
      const duration = Date.now() - startTime;

      results.push({
        test: test.name,
        success: true,
        duration,
        iterations: result.iterations
      });

      console.log(`\n✅ Test completado en ${duration}ms con ${result.iterations} iteraciones`);
    } catch (error) {
      results.push({
        test: test.name,
        success: false,
        error: error.message
      });

      console.log(`\n❌ Test falló: ${error.message}`);
    }

    // Resetear historial para el siguiente test
    agent.conversationHistory = [];

    // Pausa entre tests
    if (i < tests.length - 1) {
      console.log('\n⏳ Esperando 2 segundos antes del siguiente test...');
      await new Promise(resolve => setTimeout(resolve, 2000));
    }
  }

  // Resumen final
  console.log(`\n${'═'.repeat(80)}`);
  console.log(`\n📊 RESUMEN DE TESTS`);
  console.log(`${'═'.repeat(80)}\n`);

  const successful = results.filter(r => r.success).length;
  const failed = results.filter(r => !r.success).length;

  console.log(`Total tests: ${results.length}`);
  console.log(`✅ Exitosos: ${successful}`);
  console.log(`❌ Fallidos: ${failed}`);
  console.log();

  results.forEach((result, index) => {
    const icon = result.success ? '✅' : '❌';
    const info = result.success
      ? `${result.duration}ms, ${result.iterations} iter`
      : result.error;

    console.log(`${icon} Test ${index + 1}: ${result.test}`);
    console.log(`   ${info}`);
  });

  console.log(`\n${'═'.repeat(80)}\n`);

  // Exit code
  process.exit(failed > 0 ? 1 : 0);
}

// Verificar que LM Studio esté disponible
async function checkLMStudio() {
  const axios = require('axios');

  try {
    console.log('🔍 Verificando conexión con LM Studio...');

    await axios.get(`${agent.config.baseUrl}/health`, {
      timeout: 5000
    });

    console.log('✅ LM Studio está disponible\n');
    return true;
  } catch (error) {
    console.error('❌ Error: LM Studio no está disponible');
    console.error('   Asegúrate de que LM Studio esté ejecutándose en', agent.config.baseUrl);
    console.error('   Y que tengas un modelo cargado\n');
    return false;
  }
}

// Main
async function main() {
  const isAvailable = await checkLMStudio();

  if (!isAvailable) {
    process.exit(1);
  }

  console.log('🚀 Iniciando tests del agente autónomo...\n');
  console.log(`Configuración:`);
  console.log(`  - URL: ${agent.config.baseUrl}`);
  console.log(`  - Modelo: ${agent.config.model}`);
  console.log(`  - Max Iteraciones: ${agent.config.maxIterations}`);

  await runTests();
}

// Manejo de errores no capturados
process.on('unhandledRejection', (error) => {
  console.error('\n❌ Error no capturado:', error);
  process.exit(1);
});

// Ejecutar
main();

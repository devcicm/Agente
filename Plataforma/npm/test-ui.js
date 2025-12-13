#!/usr/bin/env node
'use strict';

// Test del nuevo sistema de UI con markdown

const {
  printLLMResult,
  printWelcomeBanner,
  printModelsList,
  printCommandMenu,
  printComparisonResults,
  printSuccess,
  printError,
  printWarning,
  printInfo,
  printHeader
} = require('./src/formatters/printer');

console.log('\n🎨 Probando el nuevo sistema de UI con formato Markdown\n');

// 1. Test de banner de bienvenida
console.log('=== TEST 1: Banner de Bienvenida ===\n');
printWelcomeBanner({
  model: 'deepseek/deepseek-r1-0528-qwen3-8b',
  baseUrl: 'http://localhost:1234',
  stream: true,
  debug: false
});

// 2. Test de mensajes de estado
console.log('\n=== TEST 2: Mensajes de Estado ===\n');
printSuccess('Conexión establecida correctamente');
printError('No se pudo conectar al servidor', 'ECONNREFUSED 127.0.0.1:1234');
printWarning('El modelo está deprecated');
printInfo('Versión del sistema: 1.0.0');

// 3. Test de lista de modelos
console.log('\n=== TEST 3: Lista de Modelos ===\n');
const testModels = [
  { id: 'deepseek/deepseek-r1-0528-qwen3-8b', name: 'DeepSeek R1 8B' },
  { id: 'mistral/mistral-7b-instruct', name: 'Mistral 7B Instruct' },
  { id: 'meta/llama-2-7b' }
];
printModelsList(testModels, 'deepseek/deepseek-r1-0528-qwen3-8b');

// 4. Test de menú de comandos
console.log('\n=== TEST 4: Menú de Comandos ===\n');
const testCommands = [
  { cmd: '/help', desc: 'Mostrar ayuda' },
  { cmd: '/models', desc: 'Listar modelos' },
  { cmd: '/stream', desc: 'Activar streaming' },
  { cmd: '/exit', desc: 'Salir' }
];
printCommandMenu(testCommands);

// 5. Test de resultado LLM con markdown
console.log('\n=== TEST 5: Resultado LLM con Markdown ===\n');
const testResult = {
  id: 'resp_12345',
  model: 'deepseek/deepseek-r1-0528-qwen3-8b',
  thinking: `Let me analyze this step by step:

1. First, I need to understand the question
2. Then break it down into components
3. Finally, formulate a clear answer

The key insight here is that we need to consider multiple factors.`,
  response: `# Análisis de la Solicitud

Aquí está mi respuesta detallada:

## Características Principales

- **Punto 1**: Esta es una característica importante
- **Punto 2**: Otra característica relevante
- **Punto 3**: Un aspecto crucial a considerar

## Ejemplo de Código

\`\`\`javascript
function hello() {
  console.log('Hello World!');
  return true;
}
\`\`\`

## Tabla de Comparación

| Característica | Opción A | Opción B |
|---------------|----------|----------|
| Velocidad     | Rápido   | Lento    |
| Memoria       | 2GB      | 4GB      |
| Costo         | $10      | $20      |

## Conclusión

En conclusión, la mejor opción depende de tus necesidades específicas. Para más información, visita [la documentación](https://ejemplo.com).

> Nota importante: Este es un ejemplo de blockquote con información relevante.`,
  usage: {
    input_tokens: 245,
    output_tokens: 1234,
    total_tokens: 1479
  },
  previous_response_id: null,
  raw: { /* datos raw */ }
};

printLLMResult(testResult, {
  showThinking: true,
  showUsage: true,
  showRaw: false,
  markdownRender: true,
  showModel: true,
  showId: true
});

// 6. Test de comparación de modelos
console.log('\n=== TEST 6: Comparación de Modelos ===\n');
const testComparison = [
  {
    model: 'deepseek/deepseek-r1-0528-qwen3-8b',
    duration_ms: 1234,
    parsed: {
      response: '## Respuesta del Modelo 1\n\nEsta es una respuesta con **formato** markdown.\n\n- Punto 1\n- Punto 2',
      usage: { input_tokens: 20, output_tokens: 50 }
    }
  },
  {
    model: 'mistral/mistral-7b-instruct',
    duration_ms: 987,
    error: 'Model not loaded'
  },
  {
    model: 'meta/llama-2-7b',
    duration_ms: 2345,
    parsed: {
      response: '## Respuesta del Modelo 3\n\nOtra respuesta con formato.\n\n```python\ndef test():\n    print("ejemplo")\n```',
      usage: { input_tokens: 20, output_tokens: 80 }
    }
  }
];

printComparisonResults(testComparison, '¿Qué es la inteligencia artificial?', {
  markdown: true
});

// 7. Test de header
console.log('\n=== TEST 7: Headers Decorativos ===\n');
printHeader('Sistema de UI Actualizado', {
  color: 'green',
  borderStyle: 'double'
});

printSuccess('Todas las pruebas de UI completadas correctamente');

console.log('\n✨ Fin de las pruebas de UI\n');

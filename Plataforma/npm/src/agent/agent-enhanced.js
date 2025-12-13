// Versión mejorada del agente con interfaz gráfica y logs
const axios = require('axios');
const { program } = require('commander');
const dotenv = require('dotenv');
const { createInterface } = require('readline');
const { stdin: input, stdout: output } = require('process');
const Logger = require('../logger');
const AgentUI = require('../ui');

// Cargar configuración
dotenv.config();

// Configuración
function normalizeBaseUrl(rawUrl) {
  try {
    const url = new URL(rawUrl);
    if (url.hostname === 'localhost') {
      url.hostname = '127.0.0.1'; // Evita resolucion a ::1 en Windows/Node
    }
    return url.toString().replace(/\/$/, '');
  } catch {
    return rawUrl;
  }
}

const config = {
  baseUrl: normalizeBaseUrl(process.env.LMSTUDIO_URL || 'http://127.0.0.1:1234'),
  model: process.env.LMSTUDIO_MODEL || 'deepseek/deepseek-r1-0528-qwen3-8b',
  debug: process.env.DEBUG_MODE === 'true',
  stream: process.env.STREAM_MODE === 'true',
  useUI: process.env.USE_UI === 'true'
};

// Inicializar logger
const logger = new Logger({ logLevel: config.debug ? 'debug' : 'info' });

const commandInfo = [
  { cmd: '/help', desc: 'Mostrar comandos' },
  { cmd: '/models', desc: 'Listar modelos disponibles' },
  { cmd: '/compare', desc: "Comparar modelos con prompt (default: hola)" },
  { cmd: '/model', desc: 'Cambiar modelo activo' },
  { cmd: '/origins', desc: 'Cambiar origen LLM (ip:puerto)' },
  { cmd: '/stream', desc: 'Activar streaming' },
  { cmd: '/nostream', desc: 'Desactivar streaming' },
  { cmd: '/debug', desc: 'Activar debug' },
  { cmd: '/nodebug', desc: 'Desactivar debug' },
  { cmd: '/test', desc: 'Probar conexion con LM Studio' },
  { cmd: '/ping', desc: 'Ping a LM Studio' },
  { cmd: '/ui', desc: 'Iniciar interfaz grafica' },
  { cmd: '/clear', desc: 'Limpiar pantalla' },
  { cmd: '/exit', desc: 'Salir' }
];

const interactiveCommands = commandInfo.map(c => c.cmd);
const knownCommands = new Set(interactiveCommands);

function completer(line) {
  const trimmed = line.trimStart();
  const first = trimmed.split(/\s+/)[0];

  if (!first.startsWith('/')) {
    return [[], line];
  }

  const firstLower = first.toLowerCase();
  const hits = interactiveCommands.filter(c => c.startsWith(firstLower));
  return [hits.length ? hits : interactiveCommands, first];
}

function showCommandMenu() {
  console.log('\nComandos disponibles:');
  commandInfo.forEach((c, i) => {
    console.log(`  ${i + 1}. ${c.cmd} - ${c.desc}`);
  });
  console.log('Escribe el numero para ejecutar o el comando directamente.');
}

// Interfaz de consola tradicional
const rl = createInterface({
  input: input,
  output: output,
  prompt: '> ',
  completer
});

// Cliente HTTP
const httpClient = axios.create({
  baseURL: config.baseUrl,
  timeout: 300000,
  headers: {
    'Content-Type': 'application/json',
    'Accept': 'application/json'
  }
});

// Inicializar UI si está activado
let agentUI;
if (config.useUI) {
  agentUI = new AgentUI(logger);
}

// Funciones principales
async function invokeLLM(prompt, useStream = false) {
  const payload = {
    model: config.model,
    input: prompt,
    stream: useStream
  };

  logger.info('Enviando solicitud a LLM: %s', prompt);

  try {
    if (useStream) {
      return await streamResponse(payload);
    } else {
      return await getResponse(payload);
    }
  } catch (error) {
    logger.error('Error al invocar LLM: %s', error.message);
    return null;
  }
}

async function getResponse(payload) {
  logger.info('Procesando solicitud en modo batch');
  
  try {
    const response = await httpClient.post('/v1/responses', payload);
    logger.info('Respuesta recibida exitosamente');
    
    return parseLLMResponse(response.data);
  } catch (error) {
    logger.error('Error en la solicitud: %s', error.message);
    throw error;
  }
}

async function streamResponse(payload) {
  logger.info('Iniciando modo streaming');
  
  try {
    const response = await httpClient.post('/v1/responses', payload, {
      responseType: 'stream'
    });
    
    return new Promise((resolve) => {
      let fullContent = '';
      
      response.data.on('data', (chunk) => {
        const text = chunk.toString();
        if (text.includes('data:')) {
          const jsonData = text.replace('data:', '').trim();
          if (jsonData !== '[DONE]') {
            try {
              const parsed = JSON.parse(jsonData);
              if (parsed.type === 'response.output_text.delta' && parsed.delta) {
                process.stdout.write(parsed.delta);
                fullContent += parsed.delta;
              }
            } catch (e) {
              // Ignorar chunks no parseables
            }
          }
        }
      });
      
      response.data.on('end', () => {
        console.log();
        logger.info('Streaming completado');
        resolve(fullContent);
      });
    });
  } catch (error) {
    logger.error('Error en streaming: %s', error.message);
    return null;
  }
}

function parseLLMResponse(responseData) {
  try {
    const output = Array.isArray(responseData?.output) ? responseData.output : [];
    const firstMessage = output[0] || {};
    const content = Array.isArray(firstMessage.content) ? firstMessage.content : [];

    const thinkingParts = [];
    const responseParts = [];

    for (const item of content) {
      if (!item || typeof item !== 'object') continue;
      const type = item.type;
      const text = item.text || '';
      if (!text) continue;

      if (type === 'output_text') {
        responseParts.push(text);
      } else if (type === 'reasoning' || type === 'thinking' || type === 'analysis' || type === 'output_reasoning') {
        thinkingParts.push(text);
      }
    }

    const thinking = thinkingParts.join('');
    const responseText = responseParts.join('');

    return {
      id: responseData.id || null,
      model: responseData.model || firstMessage.model || null,
      thinking: thinking || null,
      response: responseText || null,
      usage: responseData.usage || null,
      previous_response_id: responseData.previous_response_id || null,
      raw: responseData
    };
  } catch (error) {
    return {
      id: responseData?.id || null,
      model: responseData?.model || null,
      thinking: null,
      response: null,
      usage: responseData?.usage || null,
      previous_response_id: responseData?.previous_response_id || null,
      raw: responseData
    };
  }
}

function printLLMResult(result, options = {}) {
  if (!result) return;
  const prefix = options.prefix || '';

  if (result.thinking) {
    console.log(`\n${prefix}🧠 Thinking:`);
    console.log(`${prefix}${result.thinking}`);
  }

  console.log(`\n${prefix}💬 Respuesta:`);
  console.log(`${prefix}${result.response || '(vacía)'}`);

  if (result.usage) {
    console.log(`\n${prefix}📊 Usage:`);
    const usageText = JSON.stringify(result.usage, null, 2);
    console.log(prefix + usageText.replace(/\n/g, `\n${prefix}`));
  }

  console.log(`\n${prefix}previous_response_id: ${result.previous_response_id ?? 'null'}`);
}

async function testConnection() {
  logger.info('Probando conexión con LM Studio');
  
  try {
    const response = await httpClient.get('/health');
    logger.info('Conexión exitosa con LM Studio');
    console.log('✅ Conexión exitosa');
    console.log(`Estado: ${response.status} ${response.statusText}`);
    return true;
  } catch (error) {
    logger.error('Error de conexión: %s', error.message);
    console.error('❌ Error de conexión');
    console.error(`No se pudo conectar a ${config.baseUrl}`);
    return false;
  }
}

async function pingLmStudio() {
  logger.info('Realizando ping a LM Studio');
  const result = await logger.pingLmStudio(config.baseUrl);
  
  if (result.success) {
    console.log('✅ Ping exitoso a LM Studio');
    console.log(`Tiempo de respuesta: ${result.time}ms`);
    console.log(`Estado: ${result.status}`);
  } else {
    console.log('❌ Ping fallido a LM Studio');
    if (result.error) {
      console.log(`Error: ${result.error}`);
    }
  }
  
  return result;
}

async function listModels() {
  logger.info('Obteniendo modelos disponibles');
  
  try {
    const response = await httpClient.get('/v1/models');
    logger.info('Modelos obtenidos exitosamente');
    
    if (response.data.data && response.data.data.length > 0) {
      console.log('\n📋 Modelos disponibles:');
      response.data.data.forEach((model, index) => {
        console.log(`${index + 1}. ${model.id}`);
        if (model.name && model.name !== model.id) {
          console.log(`   Nombre: ${model.name}`);
        }
      });
    } else {
      console.log('⚠️ No se encontraron modelos');
    }
    
    return response.data.data || [];
  } catch (error) {
    logger.error('Error obteniendo modelos: %s', error.message);
    console.error('❌ Error obteniendo modelos');
    return [];
  }
}

async function getModelsSilent() {
  try {
    const response = await httpClient.get('/v1/models');
    return response.data.data || [];
  } catch (error) {
    logger.error('Error obteniendo modelos: %s', error.message);
    return [];
  }
}

async function invokeForModel(modelId, prompt) {
  const payload = {
    model: modelId,
    input: prompt,
    stream: false
  };

  const start = Date.now();
  try {
    const response = await httpClient.post('/v1/responses', payload);
    return {
      model: modelId,
      raw: response.data,
      duration_ms: Date.now() - start
    };
  } catch (error) {
    return {
      model: modelId,
      raw: null,
      error: error.response?.data?.error?.message || error.message,
      duration_ms: Date.now() - start
    };
  }
}

async function compareModels(prompt = 'hola') {
  console.log(`\n=== Comparando modelos con prompt: "${prompt}" ===`);
  const models = await getModelsSilent();

  if (!models.length) {
    console.log('⚠️ No hay modelos disponibles para comparar.');
    return;
  }

  for (let i = 0; i < models.length; i++) {
    const modelId = models[i].id || models[i];
    console.log(`\n[${i + 1}/${models.length}] Modelo: ${modelId}`);

    const result = await invokeForModel(modelId, prompt);
    if (result.error) {
      console.log(`❌ Error (${result.duration_ms}ms): ${result.error}`);
      continue;
    }

    console.log(JSON.stringify(result.raw, null, 2));
    console.log(`\n  duration_ms: ${result.duration_ms}`);
  }

  console.log('\n=== Fin de comparación ===\n');
}

async function changeModel(modelId) {
  config.model = modelId;
  logger.info('Modelo cambiado a: %s', modelId);
  console.log(`✅ Modelo cambiado a: ${modelId}`);
}

// Configuración de comandos
// Override de changeModel con soporte de alias y numero
async function changeModel(modelId) {
  const rawInput = String(modelId || '').trim();

  // Sanitizar entradas con tokens extra (ej: "s deepseek/...").
  const tokens = rawInput.split(/\s+/).filter(Boolean);
  const inputId = tokens.length > 1
    ? (tokens.find(t => t.includes('/')) || tokens[tokens.length - 1])
    : rawInput;

  if (!inputId) {
    console.log('? Uso: /model <id|numero|alias>');
    return;
  }

  let finalModelId = inputId;

  try {
    const resp = await httpClient.get('/v1/models');
    const models = resp?.data?.data || [];

    if (/^\d+$/.test(inputId)) {
      const idx = parseInt(inputId, 10) - 1;
      if (models[idx]?.id) {
        finalModelId = models[idx].id;
      } else {
        console.log('? Numero de modelo invalido. Usa /models.');
        return;
      }
    } else if (!inputId.includes('/')) {
      const lower = inputId.toLowerCase();
      const match = models.find(m => (m.id || '').toLowerCase().includes(lower));
      if (match?.id) {
        finalModelId = match.id;
      } else if (models.length > 0) {
        console.log('? Modelo no encontrado. Usa /models.');
        return;
      }
    } else if (models.length > 0 && !models.some(m => m.id === inputId)) {
      console.log('? Modelo no esta en la lista cargada; se usara igualmente.');
    }
  } catch (error) {
    logger.warn('No se pudo validar modelo: %s', error.message);
    if (!/^\d+$/.test(inputId) && !inputId.includes('/')) {
      console.log('? No se pudo validar el alias; revisa /origins o usa id completo.');
    }
  }

  config.model = finalModelId;
  logger.info('Modelo cambiado a: %s', finalModelId);
  console.log(`? Modelo cambiado a: ${finalModelId}`);
}

async function setOrigin(originInput) {
  const raw = String(originInput || '').trim();

  if (!raw) {
    console.log(`Origen actual: ${config.baseUrl}`);
    console.log('Uso: /origins <ip:puerto> o <url>');
    return;
  }

  let url;
  if (/^https?:\/\//i.test(raw)) {
    url = raw;
  } else {
    const [host, port] = raw.split(':');
    const finalPort = port || '1234';
    url = `http://${host}:${finalPort}`;
  }

  const normalized = normalizeBaseUrl(url);
  config.baseUrl = normalized;
  httpClient.defaults.baseURL = normalized;

  try {
    await httpClient.get('/v1/models', { timeout: 5000 });
    console.log(`Origen cambiado a: ${normalized}`);
  } catch (error) {
    console.log(`Origen cambiado a: ${normalized} (sin respuesta)`);
    logger.warn('No se pudo verificar el origen: %s', error.message);
  }

  logger.info('Origen LLM actualizado a: %s', normalized);
}

program
  .name('agente-npm')
  .description('Agente CLI mejorado para interacción con LLM')
  .version('1.0.0');

program
  .command('test')
  .description('Probar conexión con LM Studio')
  .action(async () => {
    await testConnection();
    process.exit(0);
  });

program
  .command('ping')
  .description('Hacer ping a LM Studio')
  .action(async () => {
    await pingLmStudio();
    process.exit(0);
  });

program
  .command('models')
  .description('Listar modelos disponibles')
  .action(async () => {
    await listModels();
    process.exit(0);
  });

program
  .command('compare [prompt...]')
  .description('Comparar todos los modelos con un prompt (default: hola)')
  .action(async (prompt) => {
    const fullPrompt = prompt && prompt.length > 0 ? prompt.join(' ') : 'hola';
    await compareModels(fullPrompt);
    process.exit(0);
  });

program
  .command('model <id>')
  .description('Cambiar modelo activo')
  .action(async (id) => {
    await changeModel(id);
    process.exit(0);
  });

program
  .command('stream')
  .description('Activar modo streaming')
  .action(() => {
    config.stream = true;
    logger.info('Modo streaming activado');
    console.log('✅ Modo streaming activado');
    process.exit(0);
  });

program
  .command('nostream')
  .description('Desactivar modo streaming')
  .action(() => {
    config.stream = false;
    logger.info('Modo streaming desactivado');
    console.log('✅ Modo streaming desactivado');
    process.exit(0);
  });

program
  .command('debug')
  .description('Activar modo debug')
  .action(() => {
    config.debug = true;
    logger.setLogLevel('debug');
    logger.info('Modo debug activado');
    console.log('✅ Modo debug activado');
    process.exit(0);
  });

program
  .command('nodebug')
  .description('Desactivar modo debug')
  .action(() => {
    config.debug = false;
    logger.setLogLevel('info');
    logger.info('Modo debug desactivado');
    console.log('✅ Modo debug desactivado');
    process.exit(0);
  });

program
  .command('ui')
  .description('Iniciar interfaz gráfica')
  .action(() => {
    config.useUI = true;
    console.log('✅ Iniciando interfaz gráfica...');
    startInteractiveMode();
  });

program
  .argument('[prompt...]')
  .description('Enviar prompt al LLM')
  .action(async (prompt) => {
    if (prompt && prompt.length > 0) {
      const fullPrompt = prompt.join(' ');
      logger.info('Prompt recibido: %s', fullPrompt);
      console.log(`\n📝 Prompt: ${fullPrompt}\n`);
      
      const result = await invokeLLM(fullPrompt, config.stream);
      
      if (result && !config.stream) {
        printLLMResult(result);
      }
      
      process.exit(0);
    }
  });

// Modo interactivo mejorado
async function startInteractiveMode() {
  console.log('\n==== Agente NPM - Modo Interactivo Mejorado ====');
  console.log(`🎯 Modelo: ${config.model}`);
  console.log(`🌐 Endpoint: ${config.baseUrl}`);
  console.log(`⚡ Modo: ${config.stream ? 'Streaming' : 'Batch'}`);
  console.log(`🐞 Debug: ${config.debug ? 'Activado' : 'Desactivado'}`);
  console.log(`📊 Modelo cargado al inicio: ${config.model}`);
  console.log();
  showCommandMenu();

  rl.prompt();

  rl.on('line', async (line) => {
    let trimmedLine = line.trim();

    if (!trimmedLine) {
      rl.prompt();
      return;
    }

    if (/^\d+$/.test(trimmedLine)) {
      const idx = parseInt(trimmedLine, 10) - 1;
      const selected = commandInfo[idx];

      if (selected) {
        trimmedLine = selected.cmd;
        console.log(`> ${trimmedLine}`);
      }
    }

    const lowerLine = trimmedLine.toLowerCase();

    if (lowerLine === '/help') {
      showCommandMenu();
      rl.prompt();
      return;
    }

    if (lowerLine === '/exit') {
      logger.info('Saliendo del programa');
      console.log('👋 Saliendo...');
      rl.close();
      await logger.close();
      process.exit(0);
    }

    if (lowerLine === '/clear') {
      console.clear();
      rl.prompt();
      return;
    }

    if (lowerLine === '/models') {
      await listModels();
      rl.prompt();
      return;
    }

    if (lowerLine.startsWith('/model')) {
      const modelId = trimmedLine.slice(6).trim();
      if (!modelId) {
        console.log(`Modelo actual: ${config.model}`);
        console.log('Uso: /model <id|numero|alias>');
      } else {
        await changeModel(modelId);
      }
      rl.prompt();
      return;
    }

    if (lowerLine.startsWith('/origins')) {
      const originArg = trimmedLine.slice(8).trim();
      await setOrigin(originArg);
      rl.prompt();
      return;
    }

    if (lowerLine === '/stream') {
      config.stream = true;
      logger.info('Modo streaming activado desde consola');
      console.log('✅ Modo streaming activado');
      rl.prompt();
      return;
    }

    if (lowerLine === '/nostream') {
      config.stream = false;
      logger.info('Modo streaming desactivado desde consola');
      console.log('✅ Modo streaming desactivado');
      rl.prompt();
      return;
    }

    if (lowerLine === '/debug') {
      config.debug = true;
      logger.setLogLevel('debug');
      logger.info('Modo debug activado desde consola');
      console.log('✅ Modo debug activado');
      rl.prompt();
      return;
    }

    if (lowerLine === '/nodebug') {
      config.debug = false;
      logger.setLogLevel('info');
      logger.info('Modo debug desactivado desde consola');
      console.log('✅ Modo debug desactivado');
      rl.prompt();
      return;
    }

    if (lowerLine === '/test') {
      await testConnection();
      rl.prompt();
      return;
    }

    if (lowerLine === '/ping') {
      await pingLmStudio();
      rl.prompt();
      return;
    }

    if (lowerLine.startsWith('/compare')) {
      const arg = trimmedLine.slice(8).trim();
      await compareModels(arg || 'hola');
      rl.prompt();
      return;
    }

    if (lowerLine === '/ui') {
      if (agentUI) {
        agentUI.start();
      } else {
        console.log('⚠️ Interfaz gráfica no disponible');
      }
      rl.prompt();
      return;
    }

    const cmdToken = trimmedLine.split(/\s+/)[0];
    const cmdTokenLower = cmdToken.toLowerCase();
    if (cmdTokenLower.startsWith('/') && !knownCommands.has(cmdTokenLower)) {
      console.log(`? Comando no reconocido: ${cmdToken}`);
      const suggestions = interactiveCommands.filter(c => c.startsWith(cmdTokenLower));
      if (suggestions.length > 0) {
        console.log(`Sugerencias: ${suggestions.join(', ')}`);
      } else {
        showCommandMenu();
      }
      rl.prompt();
      return;
    }

    // Procesar prompt normal
    logger.info('Prompt recibido: %s', trimmedLine);
    console.log(`\n📝 Prompt: ${trimmedLine}\n`);
    
    const result = await invokeLLM(trimmedLine, config.stream);
    
    if (result && !config.stream) {
      printLLMResult(result);
    }
    
    rl.prompt();
  });

  rl.on('close', () => {
    logger.info('Interfaz de consola cerrada');
    console.log('\n👋 ¡Hasta luego!');
    process.exit(0);
  });
}

// Inicio principal
async function main() {
  program.parse(process.argv);
  
  // Si no hay argumentos, iniciar modo interactivo
  if (process.argv.length === 2) {
    await startInteractiveMode();
  }
}

main().catch(error => {
  logger.error('Error inesperado: %s', error.message);
  console.error('❌ Error inesperado:');
  console.error(error.message);
  process.exit(1);
});

// Exportar para uso programático
module.exports = { invokeLLM, testConnection, pingLmStudio, listModels, changeModel, config, logger };

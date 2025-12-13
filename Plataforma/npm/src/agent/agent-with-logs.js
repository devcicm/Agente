'use strict';



const axios = require('axios');

const { program } = require('commander');

const dotenv = require('dotenv');

const { createInterface } = require('readline');

const { stdin: input, stdout: output } = require('process');

const { execSync } = require('child_process');

const Logger = require('../logger');

// Módulos de formateo estilo Claude Code
const {
  printLLMResult: printLLMResultEnhanced,
  printWelcomeBanner,
  printModelsList,
  printCommandMenu,
  printComparisonResults,
  printSuccess,
  printError,
  printWarning,
  printInfo
} = require('../formatters/printer');

// Cliente TTS VibeVoice
const VibeVoiceClient = require('../../tts/vibevoice-client');



dotenv.config();



/* ============================================================

   Terminal / Encoding / Mojibake (ASCII-safe, pkg-safe)

   ============================================================ */



function setupConsoleUtf8() {

  try { process.stdin.setEncoding('utf8'); } catch {}

  try { process.stdout.setDefaultEncoding('utf8'); } catch {}

  try { process.stderr.setDefaultEncoding('utf8'); } catch {}

  if (!process.env.LANG) process.env.LANG = 'en_US.UTF-8';

}

setupConsoleUtf8();



function detectWindowsCodePage() {

  if (process.platform !== 'win32') return null;

  try {

    const out = execSync('cmd /c chcp', { stdio: ['ignore', 'pipe', 'ignore'] }).toString('utf8');

    const m = out.match(/:\s*(\d+)/);

    return m ? parseInt(m[1], 10) : null;

  } catch {

    return null;

  }

}



function shouldAutoEnableUtf8Console() {
  const v = String(process.env.AUTO_UTF8_CONSOLE || '').trim().toLowerCase();
  if (!v) return true;
  return !['0', 'false', 'off', 'no', 'disable', 'disabled'].includes(v);
}

function tryEnableWindowsUtf8CodePage() {
  if (process.platform !== 'win32') return;
  try {
    execSync('cmd /c chcp 65001 >nul', { stdio: ['ignore', 'ignore', 'ignore'] });
  } catch {
    // best-effort
  }
}

let cp = detectWindowsCodePage();
if (process.platform === 'win32' && cp && cp !== 65001 && shouldAutoEnableUtf8Console()) {
  tryEnableWindowsUtf8CodePage();
  cp = detectWindowsCodePage();
}

const USE_EMOJI =

  process.env.USE_EMOJI === 'true'

  || (process.platform !== 'win32')

  || (cp === 65001);



// Icons en escapes unicode (pkg-safe).

// Usar emojis simples más compatibles con Windows
const ICON = USE_EMOJI ? {

  ok:     '\u2713',           // ✓ (checkmark simple, más compatible)

  err:    '\u2717',           // ✗ (X simple, más compatible)

  warn:   '\u26A0',           // ⚠ (sin variante selector)

  info:   '\u2139',           // ℹ (sin variante selector)

  prompt: '\u276F',           // ❯ (chevron derecho)

  reply:  '\u27A4',           // ➤ (flecha derecha)

  usage:  '\u2022',           // • (bullet)

  raw:    '\u2637',           // ☷ (trigram, o usar #)

  model:  '\u25C6',           // ◆ (diamante)

  net:    '\u2316',           // ⌖ (target/red)

  mode:   '\u26A1',           // ⚡ (rayo)

  bug:    '\u2699',           // ⚙ (engranaje para debug)

} : {

  ok: '[OK]',

  err: '[ERR]',

  warn: '[WARN]',

  info: '[INFO]',

  prompt: '[PROMPT]',

  reply: '[REPLY]',

  usage: '[USAGE]',

  raw: '[RAW]',

  model: '[MODEL]',

  net: '[ENDPOINT]',

  mode: '[MODE]',

  bug: '[DEBUG]',

};



function hr(title) {

  const line = '='.repeat(80);

  if (!title) return console.log(line);

  console.log(line);

  console.log(title);

  console.log(line);

}



if (process.platform === 'win32' && cp && cp !== 65001) {

  console.log(`${ICON.warn} CodePage detectado: ${cp}. Recomendado: 65001 (UTF-8).`);

  console.log('  CMD: chcp 65001');

  console.log('  PowerShell: chcp 65001; [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()');

  hr();

}



function stripControlChars(s) {

  // elimina controles que ensucian la consola

  return s.replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g, '');

}



// Detecta mojibake sin regex con caracteres raros (pkg-safe).

const MOJIBAKE_MARKERS = [

  '\u00C3',               // ?

  '\u00C2',               // ?

  '\u00E2\u20AC',         // U+00E2 U+20AC prefix (quotes/dashes mojibake)

  '\u00EF\u00BF\u00BD',   // ??? (bytes UTF-8 de U+FFFD)

  '\uFFFD',               // ?

  '\u00F0\u009F',         // e? (emoji mojibake)

];



function countMarkers(s) {

  let c = 0;

  for (const m of MOJIBAKE_MARKERS) {

    let idx = 0;

    while ((idx = s.indexOf(m, idx)) !== -1) {

      c++;

      idx += m.length;

    }

  }

  return c;

}



function looksMojibake(s) {

  s = String(s ?? '');

  for (const m of MOJIBAKE_MARKERS) if (s.includes(m)) return true;

  return false;

}



function repairLatin1ToUtf8(s) {

  try { return Buffer.from(s, 'latin1').toString('utf8'); } catch { return s; }

}



function repairBestEffort(s) {

  // Heuristica: aplicar reparacion solo si reduce markers

  const before = String(s ?? '');

  if (!looksMojibake(before)) return before;



  const cand = repairLatin1ToUtf8(before);

  if (countMarkers(cand) <= countMarkers(before)) return cand;



  return before;

}



function sanitizeForTerminal(text, { repair = true } = {}) {

  if (text == null) return '';

  let s = String(text);

  s = s.replace(/^\uFEFF/, ''); // BOM

  s = stripControlChars(s);

  if (repair) s = repairBestEffort(s);

  return s;

}

function normalizeNewlines(s) {
  return s.replace(/\r\n?/g, '\n');
}

function sanitizeForRequest(text) {
  if (text == null) return '';
  let s = String(text);
  s = s.replace(/^\uFEFF/, '');
  s = stripControlChars(s);
  s = normalizeNewlines(s);
  try { s = s.normalize('NFC'); } catch {}
  return s.trim();
}

function sanitizeModelIdForRequest(modelId) {
  let s = sanitizeForRequest(modelId);
  s = s.replace(/\s+/g, ' ');
  return s;
}

function containsCjk(text) {
  if (!text) return false;
  // CJK Unified Ideographs + Hiragana/Katakana + Hangul
  return /[\u4E00-\u9FFF\u3400-\u4DBF\u3040-\u30FF\uAC00-\uD7AF]/.test(String(text));
}

function buildLanguageInstructions(lang) {
  const l = String(lang || '').trim().toLowerCase();
  if (!l) return '';

  if (l === 'es' || l.startsWith('es-')) {
    return [
      'Responde siempre en español.',
      'No uses chino/japones/coreano (ni caracteres CJK).',
      'No incluyas razonamiento interno; responde directo.'
    ].join(' ');
  }

  if (l === 'en' || l.startsWith('en-')) {
    return [
      'Always respond in English.',
      'Do not use Chinese/Japanese/Korean (no CJK characters).',
      'Do not include internal reasoning; answer directly.'
    ].join(' ');
  }

  return `Responde siempre en el idioma: ${l}. No uses caracteres CJK.`;
}

function getInstructionsForRequest() {
  const explicit = sanitizeForRequest(config.instructions);
  if (explicit) return explicit;
  return sanitizeForRequest(buildLanguageInstructions(config.language));
}



/* ============================================================

   Config / URL

   ============================================================ */



function normalizeBaseUrl(rawUrl) {

  try {

    const url = new URL(rawUrl);

    if (url.hostname === 'localhost') url.hostname = '127.0.0.1';

    return url.toString().replace(/\/$/, '');

  } catch {

    return rawUrl;

  }

}



const config = {

  baseUrl: normalizeBaseUrl(process.env.LMSTUDIO_URL || 'http://127.0.0.1:1234'),

  model: process.env.LMSTUDIO_MODEL || 'deepseek/deepseek-r1-0528-qwen3-8b',

  instructions: process.env.LMSTUDIO_INSTRUCTIONS || '',

  language: process.env.LMSTUDIO_LANGUAGE || 'es',

  debug: process.env.DEBUG_MODE === 'true',

  stream: process.env.STREAM_MODE === 'true',

  tts: false, // Text-to-Speech activado

  showRaw: process.env.SHOW_RAW === 'true',

  showThinking: process.env.SHOW_THINKING === 'true',

  autoRetryLanguage: process.env.AUTO_RETRY_LANGUAGE !== 'false',

};



const logger = new Logger({ logLevel: config.debug ? 'debug' : 'info' });



/* ============================================================

   TTS Client Setup

   ============================================================ */

let ttsClient = null;
let ttsCounter = 1;

function getTtsClient() {
  if (!ttsClient) {
    ttsClient = new VibeVoiceClient({
      serverUrl: process.env.VIBEVOICE_URL || 'ws://localhost:3000',
      defaultVoice: 'Carter',
      debug: config.debug
    });
  }
  return ttsClient;
}

async function synthesizeIfEnabled(text) {
  if (!config.tts || !text) return;

  try {
    console.log(`\n${ICON.info} Sintetizando audio...`);

    const client = getTtsClient();
    const isHealthy = await client.checkHealth();

    if (!isHealthy) {
      console.log(`${ICON.warn} Servidor TTS no disponible (ws://localhost:3000)`);
      console.log(`${ICON.info} Inicia el servidor con: cd ../tts && ./start-vibevoice-server.bat`);
      return;
    }

    const outputFile = `tts-output-${ttsCounter++}.wav`;
    const result = await client.synthesize(text, {
      voice: 'Carter',
      outputFile
    });

    console.log(`${ICON.ok} Audio generado: ${outputFile} (${(result.audio.length / 1024).toFixed(2)} KB)`);
  } catch (error) {
    console.log(`${ICON.error} Error TTS: ${error.message}`);
  }
}



/* ============================================================

   Commands registry

   ============================================================ */



const commandInfo = [
  { cmd: '/help', desc: 'Mostrar comandos' },
  { cmd: '/models', desc: 'Listar modelos (opcional: /models <n> quick-select)' },
  { cmd: '/compare', desc: 'Comparar modelos con prompt (default: hola)' },
  { cmd: '/model', desc: 'Cambiar modelo: /model <id|numero|alias>' },
  { cmd: '/origins', desc: 'Cambiar origen: /origins <ip:puerto> o <url>' },
  { cmd: '/lang', desc: 'Forzar idioma: /lang es | /lang en | /lang off' },
  { cmd: '/instructions', desc: 'Set instrucciones (sistema): /instructions <texto> | /instructions off' },
  { cmd: '/thinking', desc: 'Mostrar thinking: /thinking on | /thinking off' },
  { cmd: '/stream', desc: 'Streaming: /stream | /stream on | /stream off | /stream on tts' },
  { cmd: '/nostream', desc: 'Alias: desactivar streaming' },
  { cmd: '/debug', desc: 'Debug: /debug | /debug on | /debug off' },
  { cmd: '/showraw', desc: 'RAW: /showraw | /showraw on | /showraw off (afecta compare/prints)' },
  { cmd: '/test', desc: 'Probar conexion con LM Studio' },
  { cmd: '/ping', desc: 'Ping a LM Studio' },
  { cmd: '/clear', desc: 'Limpiar pantalla' },
  { cmd: '/exit', desc: 'Salir' },
];



const interactiveCommands = commandInfo.map(c => c.cmd);

const knownCommands = new Set(interactiveCommands);



function completer(line) {

  const trimmed = line.trimStart();

  const first = trimmed.split(/\s+/)[0];

  if (!first.startsWith('/')) return [[], line];

  const firstLower = first.toLowerCase();

  const hits = interactiveCommands.filter(c => c.startsWith(firstLower));

  return [hits.length ? hits : interactiveCommands, first];

}



function showCommandMenu() {

  // Usar el nuevo sistema de formateo para el menú de comandos
  printCommandMenu(commandInfo);

}



/* ============================================================

   Readline

   ============================================================ */



const rl = createInterface({ input, output, prompt: '> ', completer });



/* ============================================================

   HTTP Client

   ============================================================ */



const httpClient = axios.create({

  baseURL: config.baseUrl,

  timeout: 300000,

  headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },

});



/* ============================================================

   SSE feeder (stream robusto)

   ============================================================ */



function createSseFeeder(onData) {

  let buffer = '';

  return (chunkText) => {

    buffer += chunkText;

    buffer = buffer.replace(/\r\n/g, '\n');



    while (true) {

      const sep = buffer.indexOf('\n\n');

      if (sep === -1) break;



      const rawEvent = buffer.slice(0, sep);

      buffer = buffer.slice(sep + 2);



      const lines = rawEvent.split('\n');

      const dataLines = [];

      for (const line of lines) {

        if (line.startsWith('data:')) dataLines.push(line.slice(5).trimStart());

      }

      const data = dataLines.join('\n').trim();

      if (data) onData(data);

    }

  };

}



/* ============================================================

   PARSER: thinking + respuesta (LM Studio /v1/responses)

   - thinking en output[].type="reasoning" con content[].type="reasoning_text"

   - respuesta en output[].type="message" role="assistant" con content[].type="output_text"

   ============================================================ */



function parseLMStudioResponse(responseData) {

  const outputArr = Array.isArray(responseData?.output) ? responseData.output : [];



  const thinkingParts = [];

  const responseParts = [];



  for (const out of outputArr) {

    const outType = out?.type;

    const role = out?.role;

    const content = Array.isArray(out?.content) ? out.content : [];



    if (outType === 'reasoning') {

      // algunos modelos usan summary[]

      if (Array.isArray(out.summary)) {

        for (const s of out.summary) {

          if (typeof s === 'string') thinkingParts.push(s);

          else if (s?.text) thinkingParts.push(s.text);

        }

      }

      for (const c of content) {

        const cType = c?.type;

        const text = c?.text ?? '';

        if (!text) continue;



        if (cType === 'reasoning_text' || cType === 'analysis' || cType === 'thinking' || cType === 'output_reasoning') {

          thinkingParts.push(text);

        } else if (cType === 'output_text' || cType === 'text') {

          responseParts.push(text);

        }

      }

      continue;

    }



    if (outType === 'message' && role === 'assistant') {

      for (const c of content) {

        const cType = c?.type;

        const text = c?.text ?? '';

        if (!text) continue;



        if (cType === 'output_text' || cType === 'text') responseParts.push(text);

        else if (cType === 'reasoning_text') thinkingParts.push(text);

      }

      continue;

    }



    // fallback: por si LM Studio cambia shape

    for (const c of content) {

      const text = c?.text ?? '';

      if (text) responseParts.push(text);

    }

  }



  return {

    id: responseData?.id ?? null,

    model: responseData?.model ?? null,

    thinking: thinkingParts.join('') || null,

    response: responseParts.join('') || null,

    usage: responseData?.usage ?? null,

    previous_response_id: responseData?.previous_response_id ?? null,

    raw: responseData ?? null,

  };

}



/* ============================================================

   LLM invoke

   ============================================================ */



async function invokeLLM(prompt, useStream = false) {

  const safePrompt = sanitizeForRequest(prompt);
  const payload = { model: sanitizeModelIdForRequest(config.model), input: safePrompt, stream: useStream };
  const instr = getInstructionsForRequest();
  if (instr) payload.instructions = instr;



  logger.info('Enviando solicitud a LLM: %s', safePrompt);



  try {

    if (useStream) return await streamResponse(payload);

    const first = await getResponse(payload);
    const langEnforced = Boolean(instr);
    if (
      config.autoRetryLanguage
      && langEnforced
      && first?.response
      && containsCjk(first.response)
    ) {
      const stronger = `${instr}\n\nIMPORTANTE: Responde SOLO en el idioma solicitado. No uses caracteres CJK.`;
      const retryPayload = { ...payload, instructions: stronger };
      return await getResponse(retryPayload);
    }

    return first;

  } catch (error) {

    logger.error('Error al invocar LLM: %s', error.message);

    return null;

  }

}



async function getResponse(payload) {

  logger.info('Procesando solicitud en modo batch');

  const response = await httpClient.post('/v1/responses', payload);

  logger.info('Respuesta recibida exitosamente');

  return parseLMStudioResponse(response.data);

}



async function streamResponse(payload) {

  logger.info('Iniciando modo streaming');



  const response = await httpClient.post('/v1/responses', payload, {

    responseType: 'stream',

    headers: { Accept: 'text/event-stream' },

  });



  return new Promise((resolve, reject) => {

    let fullContent = '';



    const feed = createSseFeeder((data) => {

      if (data === '[DONE]') return;

      try {

        const parsed = JSON.parse(data);



        // LM Studio: response.output_text.delta

        if (parsed?.type === 'response.output_text.delta' && typeof parsed.delta === 'string') {

          const delta = sanitizeForTerminal(parsed.delta, { repair: false });

          process.stdout.write(delta);

          fullContent += parsed.delta;

          return;

        }



        // fallback: algunos backends mandan {delta:{text:""}} o campos distintos

        const alt = parsed?.delta?.text ?? parsed?.text ?? null;

        if (typeof alt === 'string' && alt.length) {

          const delta = sanitizeForTerminal(alt, { repair: false });

          process.stdout.write(delta);

          fullContent += alt;

        }

      } catch {

        // ignorar

      }

    });



    response.data.on('data', (chunk) => feed(chunk.toString('utf8')));

    response.data.on('error', reject);

    response.data.on('end', () => {

      console.log();

      logger.info('Streaming completado');



      resolve({

        id: null,

        model: config.model,

        thinking: null,

        response: sanitizeForTerminal(fullContent, { repair: true }),

        usage: null,

        previous_response_id: null,

        raw: null,

      });

    });

  });

}



/* ============================================================

   Pretty printing (tipo agente)

   ============================================================ */



function printLLMResult(result) {

  if (!result) return;

  // Usar el nuevo sistema de formateo con markdown estilo Claude Code
  printLLMResultEnhanced(result, {
    showThinking: config.showThinking,
    showUsage: true,
    showRaw: config.showRaw,
    markdownRender: true, // Siempre renderizar markdown
    showModel: true,
    showId: true
  });

}



/* ============================================================

   Utilities

   ============================================================ */



function parseBoolWord(x) {

  const v = String(x || '').trim().toLowerCase();

  if (!v) return null;

  if (['on', 'true', '1', 'yes', 'si', 'enable', 'enabled'].includes(v)) return true;

  if (['off', 'false', '0', 'no', 'disable', 'disabled'].includes(v)) return false;

  return null;

}



function applyStreamCommand(args) {

  // Detectar '/stream on tts' o '/stream tts'
  const hasTts = args.some(arg => arg.toLowerCase() === 'tts');

  // Remover 'tts' de los args para procesar on/off
  const argsWithoutTts = args.filter(arg => arg.toLowerCase() !== 'tts');
  const b = parseBoolWord(argsWithoutTts[0]);

  if (b === null) {

    if (!args.length) {

      config.stream = !config.stream;
      config.tts = false; // Toggle stream solo, desactivar TTS

      console.log(`${ICON.ok} Streaming: ${config.stream ? 'ON' : 'OFF'}`);

      return;

    }

    console.log(`${ICON.warn} Uso: /stream | /stream on | /stream off | /stream on tts`);

    return;

  }

  config.stream = b;
  config.tts = b && hasTts; // TTS solo si stream es true Y se especificó 'tts'

  if (config.tts) {
    console.log(`${ICON.ok} Streaming + TTS: ON`);
  } else {
    console.log(`${ICON.ok} Streaming: ${config.stream ? 'ON' : 'OFF'} | TTS: OFF`);
  }

}



function applyDebugCommand(args) {

  const b = parseBoolWord(args[0]);

  if (b === null) {

    if (!args.length) {

      config.debug = !config.debug;

      logger.setLogLevel(config.debug ? 'debug' : 'info');

      console.log(`${ICON.ok} Debug: ${config.debug ? 'ON' : 'OFF'}`);

      return;

    }

    console.log(`${ICON.warn} Uso: /debug | /debug on | /debug off`);

    return;

  }

  config.debug = b;

  logger.setLogLevel(config.debug ? 'debug' : 'info');

  console.log(`${ICON.ok} Debug: ${config.debug ? 'ON' : 'OFF'}`);

}



function applyShowRawCommand(args) {

  const b = parseBoolWord(args[0]);

  if (b === null) {

    if (!args.length) {

      config.showRaw = !config.showRaw;

      console.log(`${ICON.ok} ShowRaw: ${config.showRaw ? 'ON' : 'OFF'}`);

      return;

    }

    console.log(`${ICON.warn} Uso: /showraw | /showraw on | /showraw off`);

    return;

  }

  config.showRaw = b;

  console.log(`${ICON.ok} ShowRaw: ${config.showRaw ? 'ON' : 'OFF'}`);

}

function applyThinkingCommand(args) {
  const b = parseBoolWord(args[0]);
  if (b === null) {
    if (!args.length) {
      config.showThinking = !config.showThinking;
      console.log(`${ICON.ok} Thinking: ${config.showThinking ? 'ON' : 'OFF'}`);
      return;
    }
    console.log(`${ICON.warn} Uso: /thinking on | /thinking off`);
    return;
  }
  config.showThinking = b;
  console.log(`${ICON.ok} Thinking: ${config.showThinking ? 'ON' : 'OFF'}`);
}

function applyLangCommand(args) {
  const v = String(args[0] || '').trim().toLowerCase();
  if (!v) {
    console.log(`Idioma actual: ${config.language || '(off)'}`);
    console.log('Uso: /lang es | /lang en | /lang off');
    return;
  }
  if (v === 'off' || v === 'none' || v === '0') {
    config.language = '';
    console.log(`${ICON.ok} Idioma forzado: OFF`);
    return;
  }
  config.language = v;
  console.log(`${ICON.ok} Idioma forzado: ${config.language}`);
}

function applyInstructionsCommand(args) {
  const text = args.join(' ').trim();
  if (!text) {
    console.log(`Instructions: ${sanitizeForTerminal(config.instructions || '(vacio)')}`);
    console.log('Uso: /instructions <texto> | /instructions off');
    return;
  }
  if (text.toLowerCase() === 'off' || text.toLowerCase() === 'none' || text === '0') {
    config.instructions = '';
    console.log(`${ICON.ok} Instructions: OFF`);
    return;
  }
  config.instructions = text;
  console.log(`${ICON.ok} Instructions: ON`);
}



/* ============================================================

   Commands

   ============================================================ */



async function testConnection() {

  logger.info('Probando conexion con LM Studio');

  try {

    const response = await httpClient.get('/health');

    console.log(`${ICON.ok} Conexion exitosa`);

    console.log(`Estado: ${response.status} ${response.statusText}`);

    return true;

  } catch (error) {

    logger.error('Error de conexion: %s', error.message);

    console.log(`${ICON.err} Error de conexion`);

    console.log(`No se pudo conectar a ${config.baseUrl}`);

    return false;

  }

}



async function pingLmStudio() {

  logger.info('Realizando ping a LM Studio');

  const result = await logger.pingLmStudio(config.baseUrl);



  if (result.success) {

    console.log(`${ICON.ok} Ping exitoso`);

    console.log(`Tiempo: ${result.time}ms`);

    console.log(`Estado: ${result.status}`);

  } else {

    console.log(`${ICON.err} Ping fallido`);

    if (result.error) console.log(`Error: ${sanitizeForTerminal(result.error)}`);

  }

  return result;

}



async function listModels() {

  logger.info('Obteniendo modelos disponibles');

  try {

    const response = await httpClient.get('/v1/models');

    const list = response.data?.data || [];



    if (!list.length) {

      printWarning('No se encontraron modelos.');

      return [];

    }



    // Usar el nuevo sistema de formateo para listar modelos
    printModelsList(list, config.model);

    return list;

  } catch (error) {

    logger.error('Error obteniendo modelos: %s', error.message);

    printError('Error obteniendo modelos', error.message);

    return [];

  }

}



async function getModelsSilent() {

  try {

    const response = await httpClient.get('/v1/models');

    return response.data?.data || [];

  } catch {

    return [];

  }

}



async function changeModel(modelInput) {

  const rawInput = String(modelInput || '').trim();

  if (!rawInput) {

    console.log(`${ICON.warn} Uso: /model <id|numero|alias>`);

    return;

  }



  let finalModelId = rawInput;



  try {

    const models = await getModelsSilent();



    if (/^\d+$/.test(rawInput)) {

      const idx = parseInt(rawInput, 10) - 1;

      if (models[idx]?.id) finalModelId = models[idx].id;

      else return console.log(`${ICON.warn} Numero invalido. Usa /models.`);

    } else if (!rawInput.includes('/')) {

      const lower = rawInput.toLowerCase();

      const match = models.find(m => (m.id || '').toLowerCase().includes(lower));

      if (match?.id) finalModelId = match.id;

      else return console.log(`${ICON.warn} Modelo no encontrado. Usa /models.`);

    } else if (models.length && !models.some(m => m.id === rawInput)) {

      console.log(`${ICON.warn} Modelo no esta en la lista; se usara igualmente.`);

    }

  } catch (error) {

    logger.warn('No se pudo validar modelo: %s', error.message);

  }



  config.model = finalModelId;

  logger.info('Modelo cambiado a: %s', finalModelId);

  console.log(`${ICON.ok} Modelo cambiado a: ${finalModelId}`);

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

    url = `http://${host}:${port || '1234'}`;

  }



  const normalized = normalizeBaseUrl(url);

  config.baseUrl = normalized;

  httpClient.defaults.baseURL = normalized;



  try {

    await httpClient.get('/v1/models', { timeout: 5000 });

    console.log(`${ICON.ok} Origen cambiado a: ${normalized}`);

  } catch (error) {

    console.log(`${ICON.warn} Origen cambiado a: ${normalized} (sin respuesta)`);

    logger.warn('No se pudo verificar el origen: %s', error.message);

  }



  logger.info('Origen LLM actualizado a: %s', normalized);

}



async function invokeForModel(modelId, prompt) {

  const safePrompt = sanitizeForRequest(prompt);
  const payload = { model: sanitizeModelIdForRequest(modelId), input: safePrompt, stream: false };
  const instr = getInstructionsForRequest();
  if (instr) payload.instructions = instr;

  const start = Date.now();

  try {

    const response = await httpClient.post('/v1/responses', payload);

    return { model: modelId, raw: response.data, duration_ms: Date.now() - start };

  } catch (error) {

    return {

      model: modelId,

      raw: null,

      error: error.response?.data?.error?.message || error.message,

      duration_ms: Date.now() - start,

    };

  }

}



async function compareModels(prompt = 'hola') {

  const models = await getModelsSilent();

  if (!models.length) {

    printWarning('No hay modelos disponibles para comparar.');

    return;

  }



  // Recolectar resultados de todos los modelos
  const results = [];

  for (let i = 0; i < models.length; i++) {

    const modelId = models[i].id || models[i];

    const result = await invokeForModel(modelId, prompt);



    // Parsear la respuesta si no hubo error
    if (!result.error && result.raw) {
      result.parsed = parseLMStudioResponse(result.raw);
    }



    results.push(result);

  }



  // Usar el nuevo sistema de formateo para mostrar comparación
  printComparisonResults(results, prompt, {
    markdown: !config.showRaw
  });

}



/* ============================================================

   CLI (commander)

   ============================================================ */



program

  .name('agente-npm')

  .description('Agente CLI con logs, parse thinking/response, streaming y RAW toggle')

  .version('1.2.0');



program.command('test').action(async () => { await testConnection(); process.exit(0); });

program.command('ping').action(async () => { await pingLmStudio(); process.exit(0); });

program.command('models').action(async () => { await listModels(); process.exit(0); });

program.command('compare [prompt...]').action(async (prompt) => {

  const fullPrompt = prompt && prompt.length ? prompt.join(' ') : 'hola';

  await compareModels(fullPrompt);

  process.exit(0);

});

program.command('model <id>').action(async (id) => { await changeModel(id); process.exit(0); });

program.command('origins <url>').action(async (url) => { await setOrigin(url); process.exit(0); });

program.command('stream [mode]').action(async (mode) => { applyStreamCommand([mode]); process.exit(0); });

program.command('debug [mode]').action(async (mode) => { applyDebugCommand([mode]); process.exit(0); });

program.command('lang [lang]').action(async (lang) => { applyLangCommand([lang]); process.exit(0); });

program.command('instructions [text...]').action(async (text) => { applyInstructionsCommand(text || []); process.exit(0); });

program.command('thinking [mode]').action(async (mode) => { applyThinkingCommand([mode]); process.exit(0); });

program.command('showraw [mode]').action(async (mode) => { applyShowRawCommand([mode]); process.exit(0); });



program

  .argument('[prompt...]')

  .description('Enviar prompt al LLM')

  .action(async (prompt) => {

    if (prompt && prompt.length) {

      const fullPrompt = prompt.join(' ');

      console.log(`\n${ICON.prompt} Prompt: ${sanitizeForTerminal(fullPrompt)}\n`);

      const result = await invokeLLM(fullPrompt, config.stream);

      if (result && !config.stream) printLLMResult(result);

      // Sintetizar audio si TTS está activado
      if (result && result.response) {
        await synthesizeIfEnabled(result.response);
      }

      process.exit(0);

    }

  });



/* ============================================================

   Interactive Mode

   ============================================================ */



async function startInteractiveMode() {

  // Banner de bienvenida mejorado estilo Claude Code
  printWelcomeBanner({
    model: config.model,
    baseUrl: config.baseUrl,
    stream: config.stream,
    debug: config.debug
  });

  // Información adicional
  console.log(`${ICON.raw} ShowRaw: ${config.showRaw ? 'ON' : 'OFF'}`);
  console.log(`${ICON.info} Runtime: ${process.pkg ? 'pkg' : 'node'} ${process.version}`);
  console.log(`${ICON.info} CodePage: ${cp ?? 'n/a'} (AUTO_UTF8_CONSOLE=${shouldAutoEnableUtf8Console() ? 'ON' : 'OFF'})`);
  console.log(`${ICON.info} Request sanitize: ON`);
  console.log(`${ICON.info} Language: ${config.language || 'OFF'} (AUTO_RETRY_LANGUAGE=${config.autoRetryLanguage ? 'ON' : 'OFF'})`);
  console.log(`${ICON.info} Thinking: ${config.showThinking ? 'ON' : 'OFF'} (SHOW_THINKING)`);
  if (config.instructions && sanitizeForRequest(config.instructions)) {
    console.log(`${ICON.info} Instructions: ON (LMSTUDIO_INSTRUCTIONS)`);
  }
  console.log();



  showCommandMenu();

  rl.prompt();



  rl.on('line', async (line) => {

    let trimmedLine = String(line || '').trim();

    if (!trimmedLine) return rl.prompt();



    // Ejecutar comando por numero (menu)

    if (/^\d+$/.test(trimmedLine)) {

      const idx = parseInt(trimmedLine, 10) - 1;

      const selected = commandInfo[idx];

      if (selected) {

        trimmedLine = selected.cmd;

        console.log(`> ${trimmedLine}`);

      }

    }



    const [cmdRaw, ...args] = trimmedLine.split(/\s+/);

    const cmd = cmdRaw.toLowerCase();



    if (cmd.startsWith('/') && !knownCommands.has(cmd)) {

      console.log(`${ICON.warn} Comando no reconocido: ${cmdRaw}`);

      showCommandMenu();

      return rl.prompt();

    }



    try {

      switch (cmd) {

        case '/help':

          showCommandMenu();

          return rl.prompt();



        case '/exit':

          logger.info('Saliendo del programa');

          console.log(`${ICON.info} Saliendo...`);

          rl.close();

          await logger.close();

          process.exit(0);



        case '/clear':

          console.clear();

          return rl.prompt();



        case '/models': {

          // Feature extra: /models <n> quick-select

          if (args.length && /^\d+$/.test(args[0])) {

            await changeModel(args[0]);

          } else {

            await listModels();

          }

          return rl.prompt();

        }



        case '/model':

          if (!args.length) {

            console.log(`Modelo actual: ${config.model}`);

            console.log('Uso: /model <id|numero|alias>');

          } else {

            await changeModel(args.join(' '));

          }

          return rl.prompt();



        case '/origins':

          await setOrigin(args.join(' '));

          return rl.prompt();



        case '/stream':

          applyStreamCommand(args);

          return rl.prompt();



        case '/nostream':

          config.stream = false;

          console.log(`${ICON.ok} Streaming: OFF`);

          return rl.prompt();



        case '/debug':

          applyDebugCommand(args);

          return rl.prompt();

        case '/lang':

          applyLangCommand(args);

          return rl.prompt();

        case '/instructions':

          applyInstructionsCommand(args);

          return rl.prompt();

        case '/thinking':

          applyThinkingCommand(args);

          return rl.prompt();

        case '/showraw':

          applyShowRawCommand(args);

          return rl.prompt();



        case '/test':

          await testConnection();

          return rl.prompt();



        case '/ping':

          await pingLmStudio();

          return rl.prompt();



        case '/compare':

          await compareModels(args.join(' ') || 'hola');

          return rl.prompt();



        default: {

          // Prompt normal

          logger.info('Prompt recibido: %s', trimmedLine);

          console.log(`\n${ICON.prompt} Prompt: ${sanitizeForTerminal(trimmedLine)}\n`);



          const result = await invokeLLM(trimmedLine, config.stream);

          if (result && !config.stream) printLLMResult(result);

          // Sintetizar audio si TTS está activado
          if (result && result.response) {
            await synthesizeIfEnabled(result.response);
          }

          return rl.prompt();

        }

      }

    } catch (e) {

      console.log(`${ICON.err} Error: ${sanitizeForTerminal(e.message || String(e))}`);

      return rl.prompt();

    }

  });



  rl.on('close', () => {

    logger.info('Interfaz de consola cerrada');

    console.log(`${ICON.info} Hasta luego.`);

    process.exit(0);

  });

}



/* ============================================================

   Main

   ============================================================ */



async function main() {

  program.parse(process.argv);



  // Sin args => modo interactivo

  if (process.argv.length === 2) {

    await startInteractiveMode();

  }

}



main().catch(async (error) => {

  logger.error('Error inesperado: %s', error.message);

  console.error(`${ICON.err} Error inesperado: ${sanitizeForTerminal(error.message)}`);

  try { await logger.close(); } catch {}

  process.exit(1);

});



// Export

module.exports = {

  invokeLLM,

  testConnection,

  pingLmStudio,

  listModels,

  changeModel,

  compareModels,

  config,

  logger,

};

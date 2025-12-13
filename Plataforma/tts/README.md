# VibeVoice TTS Integration

Integración completa de **Microsoft VibeVoice** (Text-to-Speech) para el proyecto Agente LLM.

## 🎯 ¿Qué es VibeVoice?

VibeVoice es un sistema TTS de última generación desarrollado por Microsoft que utiliza:
- **Modelo Qwen2.5** para procesamiento de texto
- **Diffusion model** para síntesis de audio
- **Latencia ultra-baja** (~300ms para primer chunk)
- **Streaming en tiempo real** vía WebSocket
- **9+ voces preconfiguradas** (Carter, Alice, Will, etc.)

## 📦 Contenido

```
Plataforma/tts/
├── vibevoice-client.js         # Cliente Node.js (WebSocket)
├── test-vibevoice.js            # Suite de tests
├── start-vibevoice-server.bat   # Iniciar servidor (Windows)
├── start-vibevoice-server.sh    # Iniciar servidor (Linux/Mac)
└── README.md                    # Esta documentación
```

## 🚀 Quick Start

### 1. Instalar Dependencias

El cliente Node.js requiere:

```bash
cd Plataforma/tts
npm install ws
```

### 2. Iniciar el Servidor VibeVoice

**Windows:**
```bash
start-vibevoice-server.bat
```

**Linux/Mac:**
```bash
chmod +x start-vibevoice-server.sh
./start-vibevoice-server.sh
```

El servidor se iniciará en `http://localhost:3000` con WebSocket en `ws://localhost:3000/stream`.

**Nota**: En la primera ejecución, el servidor descargará automáticamente el modelo (~2GB) desde Hugging Face.

### 3. Ejecutar Tests

```bash
node test-vibevoice.js
```

Esto generará 4 archivos de audio de prueba:
- `output-test1.wav` - Síntesis simple con voz Carter
- `output-test2.wav` - Prueba con otra voz (Alice)
- `output-test3-streaming.wav` - Streaming con callbacks
- `output-test4-long.wav` - Texto largo (~200 palabras)

## 📖 Uso del Cliente

### Ejemplo Básico

```javascript
const VibeVoiceClient = require('./vibevoice-client');

const client = new VibeVoiceClient({
  serverUrl: 'ws://localhost:3000',
  defaultVoice: 'Carter',
  debug: true
});

// Verificar servidor
const isHealthy = await client.checkHealth();
if (!isHealthy) {
  console.error('Servidor no disponible');
  process.exit(1);
}

// Sintetizar texto
const result = await client.synthesize(
  "Hello! This is a test of VibeVoice text-to-speech.",
  {
    voice: 'Carter',
    outputFile: 'output.wav'
  }
);

console.log(`Audio generado: ${result.duration}ms`);
console.log(`Chunks: ${result.chunks}`);
console.log(`Tamaño: ${(result.audio.length / 1024).toFixed(2)} KB`);
```

### Síntesis con Streaming

```javascript
const audioChunks = [];

const result = await client.synthesizeStreaming(
  "This is streaming synthesis...",
  {
    voice: 'Alice',
    onChunk: (chunk, count) => {
      audioChunks.push(chunk);
      console.log(`Chunk ${count} recibido`);
    },
    onLog: (log) => {
      if (log.event === 'backend_first_chunk_sent') {
        console.log('Primer chunk (latencia)');
      }
    }
  }
);

// Guardar audio completo
const fullAudio = Buffer.concat(audioChunks);
const wavBuffer = VibeVoiceClient.pcmToWav(fullAudio, 24000);
await fs.writeFile('streaming-output.wav', wavBuffer);
```

### Listar Voces Disponibles

```javascript
const voices = await client.listVoices();
console.log('Voces disponibles:', voices);
// Salida: ['Carter', 'Alice', 'Will', 'Aurora', ...]
```

## 🎤 Voces Disponibles

El servidor incluye las siguientes voces preconfiguradas:

| Voz | Descripción |
|-----|-------------|
| **Carter** | Voz masculina clara (default) |
| **Alice** | Voz femenina natural |
| **Will** | Voz masculina profunda |
| **Aurora** | Voz femenina suave |
| **Emily** | Voz femenina energética |
| **Jordan** | Voz neutra profesional |
| **Mason** | Voz masculina cálida |
| **Harper** | Voz femenina expresiva |
| **Riley** | Voz neutra amigable |

## ⚙️ Configuración

### Variables de Entorno

Puedes configurar el servidor con variables de entorno:

**Windows (PowerShell):**
```powershell
$env:VIBEVOICE_MODEL = "microsoft/VibeVoice-Realtime-0.5B"
$env:VIBEVOICE_PORT = "3000"
$env:VIBEVOICE_DEVICE = "cuda"  # cuda, cpu, o mps (Mac)
.\start-vibevoice-server.bat
```

**Linux/Mac (Bash):**
```bash
export VIBEVOICE_MODEL="microsoft/VibeVoice-Realtime-0.5B"
export VIBEVOICE_PORT="3000"
export VIBEVOICE_DEVICE="cuda"  # cuda, cpu, o mps (Mac)
./start-vibevoice-server.sh
```

### Opciones del Cliente

```javascript
const client = new VibeVoiceClient({
  serverUrl: 'ws://localhost:3000',  // URL del servidor
  defaultVoice: 'Carter',            // Voz predeterminada
  cfgScale: 1.5,                     // Classifier-free guidance (1.0-3.0)
  steps: 5,                          // Diffusion steps (más = mejor calidad)
  timeout: 120000,                   // Timeout en ms (default: 2 min)
  debug: false                       // Logs detallados
});
```

### Parámetros de Síntesis

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `voice` | string | 'Carter' | Nombre de la voz |
| `cfgScale` | number | 1.5 | Control de fidelidad (1.0-3.0) |
| `steps` | number | 5 | Pasos de diffusion (5-50) |
| `outputFile` | string | null | Guardar audio automáticamente |

## 🔧 Requisitos del Sistema

### Servidor (Python)

- **Python**: 3.9 o superior
- **GPU**: NVIDIA CUDA (recomendado) o Apple MPS (Mac M1+)
- **RAM**: 8GB mínimo
- **Espacio**: ~5GB (modelo + dependencias)

**Dependencias Python** (instaladas automáticamente):
```
torch >= 2.0.0
torchaudio
transformers
fastapi
uvicorn[standard]
websockets
soundfile
numpy
```

### Cliente (Node.js)

- **Node.js**: 14 o superior
- **Dependencias**:
  - `ws` (WebSocket client)

## 📊 Rendimiento

| Métrica | Valor |
|---------|-------|
| **Latencia inicial** | ~300ms |
| **Velocidad de síntesis** | ~10x tiempo real |
| **Formato de audio** | PCM16, 24kHz, mono |
| **Tamaño típico** | ~48KB por segundo |
| **Max text length** | Sin límite (streaming) |

### Benchmark en i7-12700K + RTX 3080:

```
Texto corto (20 palabras):   ~500ms total
Texto medio (100 palabras):  ~2.5s total
Texto largo (500 palabras):  ~10s total
```

## 🌐 API del Cliente

### Constructor

```javascript
new VibeVoiceClient(options)
```

### Métodos

#### `async synthesize(text, options)`

Sintetiza texto a audio (modo buffered).

**Parámetros:**
- `text` (string): Texto a sintetizar
- `options` (object):
  - `voice` (string): Nombre de la voz
  - `cfgScale` (number): CFG scale
  - `steps` (number): Diffusion steps
  - `outputFile` (string): Guardar automáticamente

**Retorna:**
```javascript
{
  audio: Buffer,        // Audio PCM16 crudo
  duration: number,     // Tiempo total en ms
  chunks: number,       // Cantidad de chunks
  logs: Array,          // Logs del servidor
  sampleRate: 24000,    // Sample rate (Hz)
  format: 'PCM16'       // Formato de audio
}
```

#### `async synthesizeStreaming(text, options)`

Sintetiza con callbacks en tiempo real.

**Opciones adicionales:**
- `onChunk(chunk, count)`: Callback por cada chunk de audio
- `onLog(log)`: Callback por cada log del servidor

#### `async listVoices()`

Lista voces disponibles.

**Retorna:** `string[]`

#### `async checkHealth()`

Verifica disponibilidad del servidor.

**Retorna:** `boolean`

#### `static pcmToWav(pcmBuffer, sampleRate)`

Convierte PCM crudo a formato WAV.

**Parámetros:**
- `pcmBuffer` (Buffer): Audio PCM16
- `sampleRate` (number): Sample rate (default: 24000)

**Retorna:** `Buffer` (WAV completo con headers)

#### `static async saveAsWav(pcmBuffer, outputPath, sampleRate)`

Guarda PCM como archivo WAV.

## 🎯 Casos de Uso

### 1. Síntesis Simple

```javascript
// Un solo audio
await client.synthesize("Hello World", { outputFile: 'hello.wav' });
```

### 2. Múltiples Voces

```javascript
const texts = [
  { text: "This is Carter", voice: "Carter" },
  { text: "This is Alice", voice: "Alice" },
  { text: "This is Will", voice: "Will" }
];

for (const { text, voice } of texts) {
  await client.synthesize(text, {
    voice,
    outputFile: `output-${voice}.wav`
  });
}
```

### 3. Streaming para UI

```javascript
// Ideal para aplicaciones interactivas
const player = new AudioPlayer();

await client.synthesizeStreaming(longText, {
  onChunk: (chunk) => {
    player.enqueue(chunk);  // Reproducir mientras se genera
  }
});
```

### 4. Generación Batch

```javascript
const articles = await loadArticles();

for (const article of articles) {
  const result = await client.synthesize(article.content, {
    voice: 'Carter',
    outputFile: `audiobooks/${article.id}.wav`
  });

  console.log(`${article.title}: ${result.duration}ms`);
}
```

## 🔒 Seguridad

### Validación de Entrada

El cliente NO valida el contenido del texto. Para producción:

```javascript
function sanitizeText(text) {
  // Limitar longitud
  if (text.length > 10000) {
    throw new Error('Texto muy largo');
  }

  // Filtrar caracteres problemáticos
  return text.replace(/[<>]/g, '');
}

const safeText = sanitizeText(userInput);
const result = await client.synthesize(safeText);
```

### Rate Limiting

Para múltiples instancias, implementa rate limiting:

```javascript
const RateLimiter = require('rate-limiter');
const limiter = new RateLimiter(10, 'minute');  // 10 requests/min

async function synthesizeWithLimit(text) {
  await limiter.wait();
  return await client.synthesize(text);
}
```

## 🐛 Troubleshooting

### Error: "Servidor no disponible"

**Causa**: El servidor VibeVoice no está ejecutándose.

**Solución**:
```bash
# Verificar que el servidor esté corriendo
curl http://localhost:3000/config

# Si no responde, iniciar servidor
./start-vibevoice-server.sh
```

### Error: "CUDA out of memory"

**Causa**: GPU sin memoria suficiente.

**Solución**:
```bash
# Usar CPU (más lento)
export VIBEVOICE_DEVICE=cpu
./start-vibevoice-server.sh

# O reducir batch size en el servidor
```

### Audio distorsionado

**Causa**: Parámetros incorrectos de síntesis.

**Solución**:
```javascript
// Aumentar diffusion steps
await client.synthesize(text, {
  steps: 20,      // Default: 5
  cfgScale: 1.8   // Default: 1.5
});
```

### WebSocket timeout

**Causa**: Texto muy largo o servidor sobrecargado.

**Solución**:
```javascript
const client = new VibeVoiceClient({
  timeout: 300000  // 5 minutos en vez de 2
});
```

## 📚 Recursos Adicionales

- **Repositorio VibeVoice**: [github.com/microsoft/VibeVoice](https://github.com/microsoft/VibeVoice)
- **Paper original**: "VibeVoice: Real-time Streaming Voice Generation"
- **Modelos Hugging Face**: [huggingface.co/microsoft/VibeVoice-Realtime-0.5B](https://huggingface.co/microsoft/VibeVoice-Realtime-0.5B)

## 🔮 Roadmap

Próximas mejoras planeadas:

- [ ] **Multi-instancia**: Load balancing con múltiples servidores
- [ ] **Cliente C#**: Para integración con engine C#
- [ ] **Caché**: Almacenar audio de frases comunes
- [ ] **Emotions**: Soporte para parámetros de emoción
- [ ] **Custom voices**: Fine-tuning de voces personalizadas
- [ ] **Audio effects**: Post-procesamiento (reverb, EQ, etc.)

## 📄 Licencia

Este cliente está licenciado bajo MIT.

VibeVoice es propiedad de Microsoft y está sujeto a su licencia original.

---

**¿Preguntas?** Consulta el código fuente en `vibevoice-client.js` o ejecuta los tests en `test-vibevoice.js`.

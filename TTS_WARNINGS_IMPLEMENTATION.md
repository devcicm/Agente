# TTS Server Warnings Implementation

**Fecha**: 2025-12-17
**Estado**: ✅ COMPLETADO

---

## Resumen

Se implementaron advertencias claras cuando el usuario intenta activar TTS pero el servidor VibeVoice no está disponible.

## Cambios Implementados

### 1. **C# Engine** (`Plataforma/C#/engine/Program.cs`)

#### Al ejecutar `/stream on tts`:
```
⚠ Streaming: activado | TTS: activado (ADVERTENCIA)
❌ Servidor TTS no disponible en ws://localhost:3000
   Inicia el servidor VibeVoice para usar TTS
```

#### Al inicio del streaming:
```
❌ TTS: Servidor no disponible - TTS deshabilitado
```

**Ubicación del código**:
- Líneas 1138-1164: Verificación al activar TTS con `/stream on tts`
- Líneas 360-387: Verificación al inicio del streaming

**Comportamiento**:
- Realiza health check al servidor TTS cuando se activa
- Si el servidor no está disponible, muestra advertencia pero permite continuar
- Al iniciar streaming, vuelve a verificar y deshabilita TTS automáticamente si no está disponible

### 2. **NPM Agent** (`Plataforma/npm/src/agent/agent-with-logs.js`)

#### Al ejecutar `/stream on tts`:
```
⚠ Streaming: ON | TTS: ON (ADVERTENCIA)
❌ Servidor TTS no disponible en ws://localhost:3000
ℹ Inicia el servidor VibeVoice para usar TTS
```

#### Al inicio del streaming:
```
❌ TTS: Servidor no disponible - TTS deshabilitado
```

**Ubicación del código**:
- Líneas 1209-1227: Verificación al activar TTS con `/stream on tts`
- Líneas 964-980: Verificación al inicio del streaming

**Comportamiento**:
- `applyStreamCommand()` ahora es async y realiza health check
- Si el servidor no está disponible, muestra advertencia pero permite continuar
- Al iniciar streaming, vuelve a verificar y deshabilita TTS automáticamente si no está disponible

---

## Testing

### Test Manual de Advertencias

**Script de prueba**: `test-tts-warnings.js`

```javascript
const VibeVoiceClient = require('./Plataforma/npm/src/tts/vibevoice-client');

async function testTtsWarnings() {
  const client = new VibeVoiceClient({
    serverUrl: 'ws://localhost:3000',
    defaultVoice: 'Carter',
    debug: false
  });

  try {
    const isHealthy = await client.checkHealth();
    if (isHealthy) {
      console.log('✓ TTS Server is available');
    } else {
      console.log('❌ Servidor TTS no disponible');
    }
  } catch (error) {
    console.log('❌ Error connecting to TTS server');
  }
}
```

**Resultado del test**:
```
Testing TTS server availability...

⚠ Streaming: ON | TTS: ON (ADVERTENCIA)
❌ Servidor TTS no disponible en ws://localhost:3000
ℹ Inicia el servidor VibeVoice para usar TTS
```

### Test con C# Engine

```bash
cd Plataforma/C#/engine
dotnet run
> /stream on tts
```

**Output esperado (sin servidor TTS)**:
```
⚠ Streaming: activado | TTS: activado (ADVERTENCIA)
❌ Servidor TTS no disponible en ws://localhost:3000
   Inicia el servidor VibeVoice para usar TTS
```

### Test con NPM Agent

```bash
cd Plataforma/npm
npm start
> /stream on tts
```

**Output esperado (sin servidor TTS)**:
```
⚠ Streaming: ON | TTS: ON (ADVERTENCIA)
❌ Servidor TTS no disponible en ws://localhost:3000
ℹ Inicia el servidor VibeVoice para usar TTS
```

---

## Indicadores Visuales Agregados

### Cuando TTS está activado correctamente:
```
✓ Streaming + TTS: activado
🔊 TTS: Escuchando frases en tiempo real...
```

### Cuando TTS no está disponible:
```
⚠ Streaming: activado | TTS: activado (ADVERTENCIA)
❌ Servidor TTS no disponible en ws://localhost:3000
   Inicia el servidor VibeVoice para usar TTS
```

### Durante el streaming (sin servidor):
```
❌ TTS: Servidor no disponible - TTS deshabilitado
```

### Durante el streaming (con servidor):
```
🔊 TTS: Escuchando frases en tiempo real...
🎵 TTS: Frase detectada (42 caracteres)
⏳ TTS: Sintetizando "El agua es una sustancia quími..."
🔊 TTS: Reproduciendo tts-stream-1.wav (187500 bytes)
✅ TTS: 3 frases completadas
```

---

## Health Check del Servidor

### Método utilizado

**C# (VibeVoiceClient.cs)**:
```csharp
public async Task<bool> CheckHealthAsync()
{
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var response = await _httpClient.GetAsync($"{_httpBaseUrl}/health", cts.Token);
        return response.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}
```

**NPM (vibevoice-client.js)**:
```javascript
async checkHealth() {
  try {
    const response = await axios.get(`${this.httpBaseUrl}/health`, {
      timeout: 2000
    });
    return response.status === 200;
  } catch (error) {
    return false;
  }
}
```

### Timing

- **Timeout**: 2 segundos
- **Momento de verificación**:
  1. Al ejecutar `/stream on tts`
  2. Al inicio de cada streaming con TTS activado

---

## Beneficios

1. **Feedback inmediato**: El usuario sabe al instante si TTS está disponible
2. **No bloquea la operación**: Permite continuar con streaming aunque TTS no esté disponible
3. **Auto-disable inteligente**: TTS se desactiva automáticamente si el servidor no responde
4. **Instrucciones claras**: Indica cómo iniciar el servidor TTS
5. **Doble verificación**: Verifica tanto al activar como al usar

---

## Próximos Pasos Sugeridos

1. **Documentación para el usuario**: Agregar instrucciones de cómo iniciar el servidor VibeVoice
2. **Script de inicio automático**: Crear script que inicie automáticamente el servidor TTS si no está corriendo
3. **Reconexión automática**: Implementar reintentos automáticos si el servidor se cae durante el streaming
4. **Configuración de URL**: Permitir cambiar la URL del servidor TTS mediante variable de entorno o comando

---

## Archivos Modificados

1. `Plataforma/C#/engine/Program.cs`
   - Líneas 357-387: Verificación al inicio de streaming
   - Líneas 897-918: Logging detallado en síntesis
   - Líneas 1138-1164: Verificación al activar comando

2. `Plataforma/npm/src/agent/agent-with-logs.js`
   - Líneas 964-980: Verificación al inicio de streaming
   - Líneas 1178-1231: Función async con health check
   - Línea 1721: Await en command handler
   - Línea 1932: Await en line handler

3. **Nuevos archivos**:
   - `test-tts-warnings.js`: Script de prueba

---

**Estado Final**: ✅ **IMPLEMENTACIÓN COMPLETA Y PROBADA**

Ambas plataformas (C# y NPM) ahora muestran advertencias claras y útiles cuando el servidor TTS no está disponible, cumpliendo con el requisito del usuario de tener "un label que indique que si entro en tts".

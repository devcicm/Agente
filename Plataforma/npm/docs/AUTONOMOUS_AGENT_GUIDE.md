# Guía del Agente Autónomo LLM

## Introducción

Esta guía explica cómo usar y personalizar el agente LLM autónomo que puede ejecutar comandos, leer/escribir archivos y usar herramientas del sistema.

## Arquitectura

```
┌─────────────────────────────────────────────────────────┐
│                        Usuario                          │
│           "Crea un script que liste archivos"          │
└────────────────────────┬────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────┐
│                   Agente Autónomo                       │
│  ┌──────────────────────────────────────────────────┐  │
│  │  Loop de Conversación                            │  │
│  │  1. Recibe mensaje usuario                       │  │
│  │  2. Llama LLM con system prompt + historial      │  │
│  │  3. Detecta tool calls en respuesta              │  │
│  │  4. Ejecuta herramientas                         │  │
│  │  5. Envía resultados al LLM                      │  │
│  │  6. Repite hasta completar tarea                 │  │
│  └──────────────────────────────────────────────────┘  │
└────────────────────────┬────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────┐
│                     Herramientas                        │
│  ┌────────┐  ┌──────────┐  ┌──────┐  ┌──────────┐    │
│  │  Bash  │  │ ReadFile │  │ Curl │  │   Grep   │    │
│  └────────┘  └──────────┘  └──────┘  └──────────┘    │
│  ┌──────────┐  ┌──────────┐                           │
│  │WriteFile │  │ EditFile │                           │
│  └──────────┘  └──────────┘                           │
└─────────────────────────────────────────────────────────┘
```

## Uso Básico

### Instalación

```bash
cd Plataforma/npm
npm install
```

### Modo Single-Shot

```bash
# Ejecutar una tarea única
node src/agent/agent-autonomous.js "Lista los archivos del directorio actual"
```

### Modo Interactivo

```bash
# Iniciar sesión interactiva
node src/agent/agent-autonomous.js

# O con debug activado
DEBUG=true node src/agent/agent-autonomous.js
```

## Ejemplos de Uso

### Ejemplo 1: Crear un archivo

**Usuario:**
```
Crea un archivo llamado hello.txt con el contenido "Hello World"
```

**Flujo del Agente:**

1. **LLM analiza la tarea:**
```
Para crear un archivo con contenido específico, necesito usar la herramienta WriteFile.
```

2. **LLM genera tool call:**
```json
{
  "tool": "WriteFile",
  "parameters": {
    "path": "hello.txt",
    "content": "Hello World"
  },
  "reasoning": "Create a new file with the specified content"
}
```

3. **Agente ejecuta la herramienta:**
```
🔧 Executing tool: WriteFile
📝 Reasoning: Create a new file with the specified content
⚙️  Parameters: { "path": "hello.txt", "content": "Hello World" }
✓ Tool result: { "success": true, "path": "hello.txt", "bytesWritten": 11 }
```

4. **LLM recibe el resultado y responde:**
```
✅ Archivo creado exitosamente. He creado "hello.txt" con el contenido "Hello World".
El archivo tiene 11 bytes de tamaño.
```

### Ejemplo 2: Analizar archivos y modificar

**Usuario:**
```
Lee el archivo package.json, identifica la versión actual y cámbiala a 2.0.0
```

**Flujo del Agente:**

**Iteración 1:**
```json
{
  "tool": "ReadFile",
  "parameters": {
    "path": "package.json"
  },
  "reasoning": "Need to read the current package.json to find the version"
}
```

Resultado: `{ "success": true, "content": "{ \"version\": \"1.0.0\", ... }" }`

**Iteración 2:**
```json
{
  "tool": "EditFile",
  "parameters": {
    "path": "package.json",
    "find": "\"version\": \"1.0.0\"",
    "replace": "\"version\": \"2.0.0\""
  },
  "reasoning": "Update the version from 1.0.0 to 2.0.0"
}
```

Resultado: `{ "success": true, "modified": true, "replacements": 1 }`

**Respuesta final:**
```
✅ He actualizado la versión en package.json de 1.0.0 a 2.0.0.
El archivo fue modificado exitosamente con 1 reemplazo.
```

### Ejemplo 3: Buscar y analizar código

**Usuario:**
```
Busca todos los archivos que contengan la palabra TODO en el directorio src
```

**Flujo:**
```json
{
  "tool": "Grep",
  "parameters": {
    "pattern": "TODO",
    "path": "./src",
    "recursive": true
  },
  "reasoning": "Search for TODO comments in source code"
}
```

### Ejemplo 4: Hacer request HTTP y procesar

**Usuario:**
```
Obtén información del usuario 'octocat' de la API de GitHub y guárdala en user.json
```

**Iteración 1 - Fetch:**
```json
{
  "tool": "Curl",
  "parameters": {
    "url": "https://api.github.com/users/octocat",
    "method": "GET",
    "headers": {
      "User-Agent": "Agent/1.0"
    }
  },
  "reasoning": "Fetch user data from GitHub API"
}
```

**Iteración 2 - Save:**
```json
{
  "tool": "WriteFile",
  "parameters": {
    "path": "user.json",
    "content": "<data from API>"
  },
  "reasoning": "Save the fetched user data to a file"
}
```

### Ejemplo 5: Operaciones complejas

**Usuario:**
```
Encuentra todos los archivos JavaScript en src/, cuenta cuántos tienen la palabra 'export',
y crea un reporte en report.txt
```

**Iteración 1 - Listar archivos:**
```json
{
  "tool": "Bash",
  "parameters": {
    "command": "find src/ -name '*.js' -type f"
  },
  "reasoning": "List all JavaScript files in src/"
}
```

**Iteración 2 - Buscar 'export':**
```json
{
  "tool": "Grep",
  "parameters": {
    "pattern": "export",
    "path": "./src",
    "recursive": true
  },
  "reasoning": "Search for 'export' keyword in JS files"
}
```

**Iteración 3 - Crear reporte:**
```json
{
  "tool": "WriteFile",
  "parameters": {
    "path": "report.txt",
    "content": "Total files: 15\nFiles with export: 12\n..."
  },
  "reasoning": "Generate report with analysis results"
}
```

## Herramientas Disponibles

### 1. Bash

Ejecuta comandos de sistema.

**Parámetros:**
- `command` (string, required): Comando a ejecutar
- `timeout` (number, optional): Timeout en ms (default: 30000)

**Ejemplo:**
```json
{
  "tool": "Bash",
  "parameters": {
    "command": "npm install axios",
    "timeout": 60000
  },
  "reasoning": "Install axios package"
}
```

**Restricciones de seguridad:**
- Bloquea comandos destructivos (`rm -rf /`, `format`, etc.)
- Requiere confirmación del usuario para operaciones peligrosas

### 2. ReadFile

Lee contenido de archivo.

**Parámetros:**
- `path` (string, required): Ruta absoluta o relativa
- `encoding` (string, optional): Encoding (default: 'utf8')

**Ejemplo:**
```json
{
  "tool": "ReadFile",
  "parameters": {
    "path": "./config.json"
  },
  "reasoning": "Read configuration file"
}
```

### 3. WriteFile

Crea o sobrescribe archivo.

**Parámetros:**
- `path` (string, required): Ruta del archivo
- `content` (string, required): Contenido a escribir
- `createDirs` (boolean, optional): Crear directorios padres si no existen

**Ejemplo:**
```json
{
  "tool": "WriteFile",
  "parameters": {
    "path": "./output/result.txt",
    "content": "Processing complete\n",
    "createDirs": true
  },
  "reasoning": "Save processing results"
}
```

### 4. EditFile

Edita archivo existente con find/replace.

**Parámetros:**
- `path` (string, required): Ruta del archivo
- `find` (string, required): Texto a buscar (puede ser regex)
- `replace` (string, required): Texto de reemplazo

**Ejemplo:**
```json
{
  "tool": "EditFile",
  "parameters": {
    "path": "./config.json",
    "find": "\"debug\": false",
    "replace": "\"debug\": true"
  },
  "reasoning": "Enable debug mode in config"
}
```

### 5. Curl

Hace request HTTP.

**Parámetros:**
- `url` (string, required): URL del request
- `method` (string, optional): Método HTTP (default: 'GET')
- `headers` (object, optional): Headers del request
- `data` (string, optional): Body para POST/PUT

**Ejemplo:**
```json
{
  "tool": "Curl",
  "parameters": {
    "url": "https://api.example.com/data",
    "method": "POST",
    "headers": {
      "Content-Type": "application/json"
    },
    "data": "{\"key\": \"value\"}"
  },
  "reasoning": "Submit data to API"
}
```

### 6. Grep

Busca patrones en archivos.

**Parámetros:**
- `pattern` (string, required): Patrón a buscar
- `path` (string, optional): Ruta donde buscar (default: '.')
- `recursive` (boolean, optional): Búsqueda recursiva (default: true)

**Ejemplo:**
```json
{
  "tool": "Grep",
  "parameters": {
    "pattern": "console.log",
    "path": "./src",
    "recursive": true
  },
  "reasoning": "Find all console.log statements"
}
```

## Configuración Avanzada

### Personalizar el System Prompt

Edita `SYSTEM_PROMPT` en `agent-autonomous.js`:

```javascript
const SYSTEM_PROMPT = `You are <YOUR_CUSTOM_IDENTITY>

<YOUR_CUSTOM_CAPABILITIES>

<YOUR_CUSTOM_RULES>
`;
```

### Agregar Nuevas Herramientas

```javascript
TOOLS.MyNewTool = async (params) => {
  const { param1, param2 } = params;

  try {
    // Tu lógica aquí
    const result = await doSomething(param1, param2);

    return {
      success: true,
      data: result
    };
  } catch (error) {
    return {
      success: false,
      error: error.message
    };
  }
};
```

Actualiza el system prompt para incluir la nueva herramienta:

```javascript
**MyNewTool** - Description of what it does
Parameters: { param1: string, param2: number }
Example: { "tool": "MyNewTool", "parameters": {...}, "reasoning": "..." }
```

### Ajustar Parámetros del Agente

```javascript
const agent = new AutonomousAgent({
  baseUrl: 'http://localhost:1234',
  model: 'your-model-id',
  maxIterations: 20,  // Máximo de iteraciones
  debug: true,        // Modo debug
  temperature: 0.1    // Temperatura del LLM (más bajo = más determinístico)
});
```

## Mejores Prácticas

### 1. System Prompts Efectivos

**✅ Bueno:**
```markdown
You are a Python code analyzer. When analyzing code:
1. First read the file
2. Identify issues
3. Suggest improvements
4. Only modify if user confirms
```

**❌ Malo:**
```markdown
You are helpful. Do things.
```

### 2. Validación de Seguridad

Siempre valida:
- Comandos destructivos
- Paths fuera del directorio de trabajo
- Requests a URLs no confiables
- Contenido de archivos antes de ejecutar como código

### 3. Manejo de Errores

```javascript
// En tu herramienta
try {
  const result = await operation();
  return { success: true, data: result };
} catch (error) {
  return {
    success: false,
    error: error.message,
    suggestion: "Try XYZ instead"
  };
}
```

### 4. Límites y Timeouts

```javascript
{
  maxIterations: 10,      // Prevenir loops infinitos
  timeout: 30000,         // Timeout por herramienta
  maxBufferSize: 10MB,    // Límite de output
  maxFileSize: 5MB        // Límite de lectura de archivos
}
```

## Limitaciones Conocidas

1. **Context Window**: El historial de conversación puede exceder el contexto del LLM
   - **Solución**: Implementar truncado o resumen automático

2. **Errores de Parsing**: El LLM puede generar JSON malformado
   - **Solución**: Retry con prompt de corrección

3. **Ambigüedad**: El LLM puede no entender tareas complejas
   - **Solución**: Pedir al usuario que clarifique

4. **Seguridad**: El LLM podría generar comandos peligrosos
   - **Solución**: Validación estricta + confirmación del usuario

## Troubleshooting

### Problema: El agente no ejecuta herramientas

**Causa**: El LLM no está generando el formato JSON correcto

**Solución:**
1. Verifica que el system prompt sea claro
2. Agrega ejemplos en el prompt
3. Usa temperatura más baja (0.0 - 0.2)

### Problema: Loop infinito

**Causa**: El agente repite la misma herramienta sin progreso

**Solución:**
1. Reduce `maxIterations`
2. Mejora el system prompt con reglas sobre cuándo terminar
3. Implementa detección de loops

### Problema: Respuestas lentas

**Causa**: Cada iteración requiere una llamada al LLM

**Solución:**
1. Usa un modelo más rápido
2. Optimiza el system prompt para ser más conciso
3. Implementa caché de respuestas comunes

## Próximas Mejoras

- [ ] Soporte para multi-tool execution (herramientas en paralelo)
- [ ] Memoria persistente entre sesiones
- [ ] Integración con MCP (Model Context Protocol)
- [ ] Sandbox para ejecución segura
- [ ] Interfaz web para visualizar el flujo del agente
- [ ] Métricas y logging avanzado

## Recursos

- [OpenAI Function Calling](https://platform.openai.com/docs/guides/function-calling)
- [Anthropic Tool Use](https://docs.anthropic.com/claude/docs/tool-use)
- [LangChain Agents](https://python.langchain.com/docs/modules/agents/)

---

**Versión:** 1.0.0
**Última actualización:** 2025-12-13
**Autor:** Claude Sonnet 4.5

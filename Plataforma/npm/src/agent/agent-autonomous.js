'use strict';

/**
 * Agente LLM Autónomo con Capacidad de Ejecutar Herramientas
 *
 * Este agente implementa un loop de conversación donde el LLM puede:
 * - Ejecutar comandos bash
 * - Leer y escribir archivos
 * - Hacer requests HTTP
 * - Buscar patrones en archivos
 */

const axios = require('axios');
const { execSync } = require('child_process');
const fs = require('fs').promises;
const path = require('path');

// ============================================================
// SYSTEM PROMPT - El "Adoctrinamiento" del Agente
// ============================================================

const SYSTEM_PROMPT = `You are Code Assistant, an autonomous AI agent with access to system tools.

## Available Tools

You can invoke tools by responding with a JSON object in this format:

\`\`\`json
{
  "tool": "ToolName",
  "parameters": { ... },
  "reasoning": "why you're using this tool"
}
\`\`\`

### Available Tools:

**Bash** - Execute system commands
Parameters: { command: string, timeout?: number }
Example: { "tool": "Bash", "parameters": { "command": "ls -la" }, "reasoning": "List files" }

**ReadFile** - Read file contents
Parameters: { path: string }
Example: { "tool": "ReadFile", "parameters": { "path": "/path/to/file.txt" }, "reasoning": "Read config" }

**WriteFile** - Create or overwrite file
Parameters: { path: string, content: string }
Example: { "tool": "WriteFile", "parameters": { "path": "/path/file.txt", "content": "data" }, "reasoning": "Save output" }

**EditFile** - Edit file with find/replace
Parameters: { path: string, find: string, replace: string }
Example: { "tool": "EditFile", "parameters": { "path": "/file.txt", "find": "old", "replace": "new" }, "reasoning": "Update value" }

**Curl** - Make HTTP request
Parameters: { url: string, method?: string, headers?: object, data?: string }
Example: { "tool": "Curl", "parameters": { "url": "https://api.example.com/data" }, "reasoning": "Fetch data" }

**Grep** - Search for patterns
Parameters: { pattern: string, path?: string }
Example: { "tool": "Grep", "parameters": { "pattern": "TODO", "path": "./src" }, "reasoning": "Find todos" }

## Rules

1. ALWAYS think step-by-step before acting
2. Use ONE tool at a time
3. Wait for tool results before deciding next action
4. ASK before destructive operations (rm, del, DROP, etc.)
5. Validate file paths and escape shell arguments properly
6. If a tool fails, analyze the error and try an alternative approach

## Workflow

For each user request:
1. Understand what they want
2. Plan the steps needed
3. Execute tools one by one
4. Verify results
5. Report back to user

When you don't need to use a tool, respond normally with helpful information.`;

// ============================================================
// HERRAMIENTAS DISPONIBLES
// ============================================================

const TOOLS = {
  /**
   * Ejecutar comando bash
   */
  Bash: async (params) => {
    const { command, timeout = 30000 } = params;

    if (!command) {
      throw new Error('Command parameter is required');
    }

    // Validación de seguridad básica
    const dangerousPatterns = [
      /rm\s+-rf\s+\//,
      /format\s+/i,
      /del\s+\/[fqs]/i,
      /DROP\s+TABLE/i
    ];

    for (const pattern of dangerousPatterns) {
      if (pattern.test(command)) {
        throw new Error('Potentially dangerous command blocked. Please confirm with user first.');
      }
    }

    try {
      const output = execSync(command, {
        encoding: 'utf8',
        timeout,
        maxBuffer: 10 * 1024 * 1024, // 10MB
        shell: process.platform === 'win32' ? 'cmd.exe' : '/bin/bash'
      });

      return {
        success: true,
        output: output.trim(),
        exitCode: 0
      };
    } catch (error) {
      return {
        success: false,
        output: error.stdout?.toString() || '',
        error: error.stderr?.toString() || error.message,
        exitCode: error.status || 1
      };
    }
  },

  /**
   * Leer archivo
   */
  ReadFile: async (params) => {
    const { path: filePath, encoding = 'utf8' } = params;

    if (!filePath) {
      throw new Error('Path parameter is required');
    }

    try {
      const content = await fs.readFile(filePath, encoding);
      const stats = await fs.stat(filePath);

      return {
        success: true,
        content,
        size: stats.size,
        path: filePath
      };
    } catch (error) {
      return {
        success: false,
        error: error.message,
        path: filePath
      };
    }
  },

  /**
   * Escribir archivo
   */
  WriteFile: async (params) => {
    const { path: filePath, content, createDirs = false } = params;

    if (!filePath || content === undefined) {
      throw new Error('Path and content parameters are required');
    }

    try {
      if (createDirs) {
        const dir = path.dirname(filePath);
        await fs.mkdir(dir, { recursive: true });
      }

      await fs.writeFile(filePath, content, 'utf8');

      return {
        success: true,
        path: filePath,
        bytesWritten: Buffer.byteLength(content, 'utf8')
      };
    } catch (error) {
      return {
        success: false,
        error: error.message,
        path: filePath
      };
    }
  },

  /**
   * Editar archivo (find/replace)
   */
  EditFile: async (params) => {
    const { path: filePath, find, replace } = params;

    if (!filePath || !find || replace === undefined) {
      throw new Error('Path, find, and replace parameters are required');
    }

    try {
      // Leer archivo
      const content = await fs.readFile(filePath, 'utf8');

      // Reemplazar
      const newContent = content.replace(new RegExp(find, 'g'), replace);

      if (content === newContent) {
        return {
          success: true,
          modified: false,
          message: 'No matches found, file not modified'
        };
      }

      // Escribir de vuelta
      await fs.writeFile(filePath, newContent, 'utf8');

      return {
        success: true,
        modified: true,
        path: filePath,
        replacements: (content.match(new RegExp(find, 'g')) || []).length
      };
    } catch (error) {
      return {
        success: false,
        error: error.message,
        path: filePath
      };
    }
  },

  /**
   * Hacer request HTTP
   */
  Curl: async (params) => {
    const { url, method = 'GET', headers = {}, data = null, timeout = 30000 } = params;

    if (!url) {
      throw new Error('URL parameter is required');
    }

    try {
      const response = await axios({
        url,
        method,
        headers,
        data,
        timeout,
        maxContentLength: 10 * 1024 * 1024, // 10MB
        validateStatus: () => true // Accept any status code
      });

      return {
        success: response.status >= 200 && response.status < 300,
        status: response.status,
        statusText: response.statusText,
        headers: response.headers,
        data: response.data,
        url
      };
    } catch (error) {
      return {
        success: false,
        error: error.message,
        url
      };
    }
  },

  /**
   * Buscar patrones (grep)
   */
  Grep: async (params) => {
    const { pattern, path: searchPath = '.', recursive = true } = params;

    if (!pattern) {
      throw new Error('Pattern parameter is required');
    }

    try {
      const grepCmd = process.platform === 'win32'
        ? `findstr /s /n /i "${pattern}" "${searchPath}\\*"`
        : `grep -${recursive ? 'r' : ''}n "${pattern}" "${searchPath}" 2>/dev/null`;

      const output = execSync(grepCmd, {
        encoding: 'utf8',
        maxBuffer: 5 * 1024 * 1024, // 5MB
        shell: true
      });

      const lines = output.trim().split('\n').filter(Boolean);

      return {
        success: true,
        matches: lines.length,
        results: lines.slice(0, 100), // Limit to 100 results
        pattern,
        path: searchPath
      };
    } catch (error) {
      // grep returns exit code 1 when no matches found
      if (error.status === 1) {
        return {
          success: true,
          matches: 0,
          results: [],
          pattern,
          path: searchPath
        };
      }

      return {
        success: false,
        error: error.message,
        pattern
      };
    }
  }
};

// ============================================================
// AGENTE AUTÓNOMO - Loop Principal
// ============================================================

class AutonomousAgent {
  constructor(config = {}) {
    this.config = {
      baseUrl: config.baseUrl || 'http://localhost:1234',
      model: config.model || 'deepseek/deepseek-r1-0528-qwen3-8b',
      maxIterations: config.maxIterations || 10,
      debug: config.debug || false,
      ...config
    };

    this.conversationHistory = [];
    this.httpClient = axios.create({
      baseURL: this.config.baseUrl,
      timeout: 120000
    });
  }

  /**
   * Añadir mensaje al historial
   */
  addMessage(role, content) {
    this.conversationHistory.push({ role, content });

    if (this.config.debug) {
      console.log(`\n[${role.toUpperCase()}]:`);
      console.log(content);
      console.log('---');
    }
  }

  /**
   * Llamar al LLM
   */
  async callLLM(userMessage = null) {
    if (userMessage) {
      this.addMessage('user', userMessage);
    }

    const messages = [
      { role: 'system', content: SYSTEM_PROMPT },
      ...this.conversationHistory
    ];

    try {
      const response = await this.httpClient.post('/v1/chat/completions', {
        model: this.config.model,
        messages,
        temperature: 0.1, // Más determinístico para agente
        max_tokens: 2000
      });

      const assistantMessage = response.data.choices[0].message.content;
      this.addMessage('assistant', assistantMessage);

      return assistantMessage;
    } catch (error) {
      throw new Error(`LLM call failed: ${error.message}`);
    }
  }

  /**
   * Detectar y ejecutar tool call
   */
  async executeToolIfPresent(message) {
    // Buscar JSON en el mensaje
    const jsonMatch = message.match(/```json\s*(\{[\s\S]*?\})\s*```/) ||
                      message.match(/(\{[\s\S]*?"tool"[\s\S]*?\})/);

    if (!jsonMatch) {
      return null; // No hay tool call
    }

    try {
      const toolCall = JSON.parse(jsonMatch[1]);

      if (!toolCall.tool || !toolCall.parameters) {
        return null;
      }

      const toolName = toolCall.tool;
      const toolFunc = TOOLS[toolName];

      if (!toolFunc) {
        return {
          success: false,
          error: `Unknown tool: ${toolName}`
        };
      }

      console.log(`\n🔧 Executing tool: ${toolName}`);
      console.log(`📝 Reasoning: ${toolCall.reasoning || 'N/A'}`);
      console.log(`⚙️  Parameters:`, JSON.stringify(toolCall.parameters, null, 2));

      const result = await toolFunc(toolCall.parameters);

      console.log(`✓ Tool result:`, JSON.stringify(result, null, 2));

      return {
        tool: toolName,
        result,
        reasoning: toolCall.reasoning
      };
    } catch (error) {
      return {
        success: false,
        error: `Tool execution failed: ${error.message}`
      };
    }
  }

  /**
   * Ejecutar tarea completa (loop autónomo)
   */
  async executeTask(userRequest) {
    console.log(`\n${'='.repeat(80)}`);
    console.log(`🤖 AGENTE AUTÓNOMO INICIADO`);
    console.log(`📋 Tarea: ${userRequest}`);
    console.log(`${'='.repeat(80)}\n`);

    let iteration = 0;
    let lastMessage = await this.callLLM(userRequest);

    while (iteration < this.config.maxIterations) {
      iteration++;

      console.log(`\n[Iteración ${iteration}/${this.config.maxIterations}]`);

      // Intentar ejecutar herramienta si está presente
      const toolExecution = await this.executeToolIfPresent(lastMessage);

      if (!toolExecution) {
        // No hay más herramientas, el agente terminó
        console.log('\n✅ Agente terminó (no hay más herramientas que ejecutar)');
        break;
      }

      // Enviar resultado de la herramienta al LLM
      const toolResultMessage = `Tool execution result:\n\`\`\`json\n${JSON.stringify(toolExecution, null, 2)}\n\`\`\``;
      lastMessage = await this.callLLM(toolResultMessage);
    }

    if (iteration >= this.config.maxIterations) {
      console.log('\n⚠️  Límite de iteraciones alcanzado');
    }

    console.log(`\n${'='.repeat(80)}`);
    console.log(`📊 Resumen:`);
    console.log(`   - Iteraciones: ${iteration}`);
    console.log(`   - Mensajes: ${this.conversationHistory.length}`);
    console.log(`${'='.repeat(80)}\n`);

    return {
      finalResponse: lastMessage,
      iterations: iteration,
      conversationHistory: this.conversationHistory
    };
  }

  /**
   * Modo interactivo
   */
  async startInteractive() {
    const readline = require('readline');
    const rl = readline.createInterface({
      input: process.stdin,
      output: process.stdout,
      prompt: '\n💬 You: '
    });

    console.log('\n🤖 Agente Autónomo Interactivo');
    console.log('Escribe "exit" para salir\n');

    rl.prompt();

    rl.on('line', async (line) => {
      const input = line.trim();

      if (!input) {
        rl.prompt();
        return;
      }

      if (input.toLowerCase() === 'exit') {
        console.log('\n👋 Saliendo...');
        rl.close();
        process.exit(0);
      }

      try {
        await this.executeTask(input);
      } catch (error) {
        console.error(`\n❌ Error: ${error.message}`);
      }

      rl.prompt();
    });
  }
}

// ============================================================
// EXPORTAR Y CLI
// ============================================================

module.exports = { AutonomousAgent, TOOLS, SYSTEM_PROMPT };

// CLI si se ejecuta directamente
if (require.main === module) {
  const agent = new AutonomousAgent({
    debug: process.env.DEBUG === 'true'
  });

  const task = process.argv.slice(2).join(' ');

  if (task) {
    // Modo single-shot
    agent.executeTask(task)
      .then(() => process.exit(0))
      .catch(error => {
        console.error(`Error: ${error.message}`);
        process.exit(1);
      });
  } else {
    // Modo interactivo
    agent.startInteractive();
  }
}

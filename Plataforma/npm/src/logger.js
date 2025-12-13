// Sistema de logs mejorado para el agente
const { createWriteStream, existsSync, mkdirSync } = require('fs');
const { join, dirname } = require('path');
const os = require('os');
const { format } = require('util');

function resolveLogPaths(options = {}) {
  if (options.logFile) {
    return { logFile: options.logFile, logDir: dirname(options.logFile) };
  }

  const isPkg = typeof process.pkg !== 'undefined';
  const logDir =
    options.logDir ||
    process.env.AGENTE_LOG_DIR ||
    (isPkg
      ? join(os.homedir(), '.agente-npm', 'logs')
      : join(__dirname, '..', 'logs'));

  return { logDir, logFile: join(logDir, 'agente.log') };
}

class Logger {
  constructor(options = {}) {
    const { logFile, logDir } = resolveLogPaths(options);
    this.logFile = logFile;
    this.maxLogSize = options.maxLogSize || 1024 * 1024; // 1MB
    this.logLevel = options.logLevel || 'info';
    this.logQueue = [];
    this.isProcessing = false;
    
    // Crear directorio de logs si no existe
    if (!existsSync(logDir)) {
      try {
        mkdirSync(logDir, { recursive: true });
      } catch (err) {
        console.error('No se pudo crear directorio de logs:', err.message);
      }
    }
    
    // Crear stream de escritura
    try {
      this.logStream = createWriteStream(this.logFile, { flags: 'a' });

      // Manejar errores del stream
      this.logStream.on('error', (err) => {
        console.error('Error en stream de logs:', err.message);
      });
    } catch (err) {
      this.logStream = null;
      console.error('No se pudo inicializar el stream de logs:', err.message);
    }
  }
  
  setLogLevel(level) {
    const allowed = { error: true, warn: true, info: true, debug: true };
    if (allowed[level]) {
      this.logLevel = level;
    }
  }

  log(level, message, ...args) {
    const timestamp = new Date().toISOString();
    const formattedMessage = args.length > 0 ? format(message, ...args) : message;
    const logEntry = `[${timestamp}] [${level.toUpperCase()}] ${formattedMessage}\n`;
    
    // Filtrar por nivel de log
    const levels = { error: 0, warn: 1, info: 2, debug: 3 };
    if (levels[level] > levels[this.logLevel]) {
      return;
    }
    
    // Agregar a la cola
    this.logQueue.push(logEntry);
    
    // Procesar cola si no está procesando
    if (!this.isProcessing) {
      this.processQueue();
    }
    
    // Mostrar en consola según el nivel
    if (level === 'error') {
      console.error(`[${timestamp}] [ERROR] ${formattedMessage}`);
    } else if (level === 'warn') {
      console.warn(`[${timestamp}] [WARN] ${formattedMessage}`);
    } else if (level === 'info') {
      console.log(`[${timestamp}] [INFO] ${formattedMessage}`);
    } else if (level === 'debug' && this.logLevel === 'debug') {
      console.debug(`[${timestamp}] [DEBUG] ${formattedMessage}`);
    }
  }
  
  info(message, ...args) {
    this.log('info', message, ...args);
  }
  
  warn(message, ...args) {
    this.log('warn', message, ...args);
  }
  
  error(message, ...args) {
    this.log('error', message, ...args);
  }
  
  debug(message, ...args) {
    this.log('debug', message, ...args);
  }
  
  processQueue() {
    this.isProcessing = true;
    
    while (this.logQueue.length > 0) {
      const entry = this.logQueue.shift();
      
      try {
        if (this.logStream) {
          this.logStream.write(entry);
        }
      } catch (err) {
        console.error('Error escribiendo en log:', err.message);
      }
    }
    
    this.isProcessing = false;
  }
  
  close() {
    return new Promise((resolve) => {
      this.logStream.end(() => {
        resolve();
      });
    });
  }
  
  // Ping a LM Studio
  async pingLmStudio(baseUrl) {
    const startTime = Date.now();
    this.info(`Iniciando ping a LM Studio en ${baseUrl}`);
    
    try {
      const response = await fetch(`${baseUrl}/health`, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json'
        }
      });
      
      const duration = Date.now() - startTime;
      
      if (response.ok) {
        this.info(`Ping exitoso a LM Studio - Tiempo: ${duration}ms`);
        return { success: true, time: duration, status: response.status };
      } else {
        this.warn(`Ping fallido a LM Studio - Estado: ${response.status}`);
        return { success: false, time: duration, status: response.status };
      }
    } catch (error) {
      this.error(`Error en ping a LM Studio: ${error.message}`);
      return { success: false, time: Date.now() - startTime, error: error.message };
    }
  }
}

module.exports = Logger;

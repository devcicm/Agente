'use strict';

/**
 * Cliente Node.js para Microsoft VibeVoice TTS
 *
 * Este cliente se conecta al servidor VibeVoice (FastAPI + WebSocket)
 * y permite sintetizar texto a voz en tiempo real.
 */

const WebSocket = require('ws');
const EventEmitter = require('events');
const fs = require('fs').promises;
const path = require('path');

class VibeVoiceClient extends EventEmitter {
  constructor(options = {}) {
    super();

    this.config = {
      serverUrl: options.serverUrl || process.env.VIBEVOICE_URL || 'ws://localhost:3000',
      defaultVoice: options.defaultVoice || 'Carter',
      cfgScale: options.cfgScale || 1.5,
      steps: options.steps || 5,
      timeout: options.timeout || 120000,
      debug: options.debug || false,
      ...options
    };

    this.availableVoices = [];
  }

  /**
   * Obtiene la configuración del servidor (voces disponibles)
   */
  async getConfig() {
    try {
      const httpUrl = this.config.serverUrl.replace('ws://', 'http://').replace('wss://', 'https://');
      const response = await fetch(`${httpUrl}/config`);

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }

      const config = await response.json();
      this.availableVoices = config.voices || [];

      if (this.config.debug) {
        console.log('[VibeVoice] Config:', config);
      }

      return config;
    } catch (error) {
      throw new Error(`Failed to get server config: ${error.message}`);
    }
  }

  /**
   * Lista las voces disponibles
   */
  async listVoices() {
    if (this.availableVoices.length === 0) {
      await this.getConfig();
    }

    return this.availableVoices;
  }

  /**
   * Sintetiza texto a audio
   *
   * @param {string} text - Texto a sintetizar
   * @param {object} options - Opciones de síntesis
   * @returns {Promise<Buffer>} Audio PCM16 a 24kHz
   */
  async synthesize(text, options = {}) {
    const {
      voice = this.config.defaultVoice,
      cfgScale = this.config.cfgScale,
      steps = this.config.steps,
      outputFile = null
    } = options;

    if (!text || typeof text !== 'string') {
      throw new Error('Text parameter is required and must be a string');
    }

    return new Promise((resolve, reject) => {
      const params = new URLSearchParams({
        text: text,
        voice: voice,
        cfg: cfgScale.toString(),
        steps: steps.toString()
      });

      const wsUrl = `${this.config.serverUrl}/stream?${params.toString()}`;

      if (this.config.debug) {
        console.log('[VibeVoice] Connecting to:', wsUrl);
      }

      const ws = new WebSocket(wsUrl);
      const audioChunks = [];
      const logs = [];
      let startTime = Date.now();

      const timeout = setTimeout(() => {
        ws.close();
        reject(new Error('Synthesis timeout'));
      }, this.config.timeout);

      ws.on('open', () => {
        this.emit('connected');
        if (this.config.debug) {
          console.log('[VibeVoice] WebSocket connected');
        }
      });

      ws.on('message', (data) => {
        if (typeof data === 'string') {
          // Log message from server
          try {
            const log = JSON.parse(data);
            logs.push(log);

            this.emit('log', log);

            if (this.config.debug) {
              console.log(`[VibeVoice] ${log.event}:`, log.data || '');
            }

            // Emit specific events
            if (log.event === 'backend_first_chunk_sent') {
              const latency = Date.now() - startTime;
              this.emit('first-chunk', latency);
            }
          } catch (error) {
            if (this.config.debug) {
              console.warn('[VibeVoice] Failed to parse log:', data);
            }
          }
        } else if (Buffer.isBuffer(data)) {
          // Audio binary data
          audioChunks.push(data);
          this.emit('audio-chunk', data);

          if (this.config.debug && audioChunks.length % 10 === 0) {
            console.log(`[VibeVoice] Received ${audioChunks.length} audio chunks`);
          }
        }
      });

      ws.on('close', async () => {
        clearTimeout(timeout);
        this.emit('completed');

        const totalTime = Date.now() - startTime;

        if (audioChunks.length === 0) {
          return reject(new Error('No audio data received'));
        }

        const audioBuffer = Buffer.concat(audioChunks);

        if (this.config.debug) {
          console.log('[VibeVoice] Synthesis complete');
          console.log(`  - Total time: ${totalTime}ms`);
          console.log(`  - Audio chunks: ${audioChunks.length}`);
          console.log(`  - Audio size: ${audioBuffer.length} bytes`);
          console.log(`  - Logs: ${logs.length}`);
        }

        // Guardar a archivo si se especificó
        if (outputFile) {
          try {
            await fs.writeFile(outputFile, audioBuffer);
            if (this.config.debug) {
              console.log(`[VibeVoice] Saved to: ${outputFile}`);
            }
          } catch (error) {
            console.error(`[VibeVoice] Failed to save file: ${error.message}`);
          }
        }

        resolve({
          audio: audioBuffer,
          duration: totalTime,
          chunks: audioChunks.length,
          logs: logs,
          sampleRate: 24000,  // VibeVoice usa 24kHz
          format: 'PCM16'
        });
      });

      ws.on('error', (error) => {
        clearTimeout(timeout);
        this.emit('error', error);
        reject(new Error(`WebSocket error: ${error.message}`));
      });
    });
  }

  /**
   * Sintetiza con streaming (callbacks para cada chunk)
   */
  async synthesizeStreaming(text, options = {}) {
    const {
      voice = this.config.defaultVoice,
      cfgScale = this.config.cfgScale,
      steps = this.config.steps,
      onChunk = null,
      onLog = null
    } = options;

    if (!text || typeof text !== 'string') {
      throw new Error('Text parameter is required');
    }

    return new Promise((resolve, reject) => {
      const params = new URLSearchParams({
        text: text,
        voice: voice,
        cfg: cfgScale.toString(),
        steps: steps.toString()
      });

      const wsUrl = `${this.config.serverUrl}/stream?${params.toString()}`;
      const ws = new WebSocket(wsUrl);
      const logs = [];
      let chunkCount = 0;
      const startTime = Date.now();

      const timeout = setTimeout(() => {
        ws.close();
        reject(new Error('Synthesis timeout'));
      }, this.config.timeout);

      ws.on('message', (data) => {
        if (typeof data === 'string') {
          try {
            const log = JSON.parse(data);
            logs.push(log);
            if (onLog) onLog(log);
          } catch {}
        } else if (Buffer.isBuffer(data)) {
          chunkCount++;
          if (onChunk) onChunk(data, chunkCount);
        }
      });

      ws.on('close', () => {
        clearTimeout(timeout);
        resolve({
          duration: Date.now() - startTime,
          chunks: chunkCount,
          logs: logs
        });
      });

      ws.on('error', (error) => {
        clearTimeout(timeout);
        reject(error);
      });
    });
  }

  /**
   * Convierte audio PCM16 a WAV
   */
  static pcmToWav(pcmBuffer, sampleRate = 24000, numChannels = 1, bitsPerSample = 16) {
    const byteRate = (sampleRate * numChannels * bitsPerSample) / 8;
    const blockAlign = (numChannels * bitsPerSample) / 8;
    const dataSize = pcmBuffer.length;

    const headerSize = 44;
    const wavBuffer = Buffer.alloc(headerSize + dataSize);

    // RIFF header
    wavBuffer.write('RIFF', 0);
    wavBuffer.writeUInt32LE(36 + dataSize, 4);
    wavBuffer.write('WAVE', 8);

    // fmt chunk
    wavBuffer.write('fmt ', 12);
    wavBuffer.writeUInt32LE(16, 16); // fmt chunk size
    wavBuffer.writeUInt16LE(1, 20);  // PCM format
    wavBuffer.writeUInt16LE(numChannels, 22);
    wavBuffer.writeUInt32LE(sampleRate, 24);
    wavBuffer.writeUInt32LE(byteRate, 28);
    wavBuffer.writeUInt16LE(blockAlign, 32);
    wavBuffer.writeUInt16LE(bitsPerSample, 34);

    // data chunk
    wavBuffer.write('data', 36);
    wavBuffer.writeUInt32LE(dataSize, 40);
    pcmBuffer.copy(wavBuffer, 44);

    return wavBuffer;
  }

  /**
   * Guarda audio como archivo WAV
   */
  static async saveAsWav(pcmBuffer, outputPath, sampleRate = 24000) {
    const wavBuffer = VibeVoiceClient.pcmToWav(pcmBuffer, sampleRate);
    await fs.writeFile(outputPath, wavBuffer);
    return outputPath;
  }

  /**
   * Verifica si el servidor está disponible
   */
  async checkHealth() {
    try {
      const httpUrl = this.config.serverUrl.replace('ws://', 'http://').replace('wss://', 'https://');
      const response = await fetch(`${httpUrl}/config`, {
        timeout: 5000
      });

      return response.ok;
    } catch (error) {
      return false;
    }
  }
}

module.exports = VibeVoiceClient;

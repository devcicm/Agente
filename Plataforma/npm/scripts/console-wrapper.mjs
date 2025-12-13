#!/usr/bin/env node

/**
 * Wrapper de consola para agente NPM
 * Este archivo permite ejecutar el agente como un programa de consola nativo
 * sin depender de Node.js instalado (cuando se compila con pkg)
 */

import { spawn } from 'child_process';
import path from 'path';
import { fileURLToPath } from 'url';

// Obtener ruta del archivo actual
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Ruta al ejecutable de Node.js (para desarrollo)
// Cuando se compila con pkg, esto se reemplaza por el binario compilado
const nodePath = process.execPath;
const scriptPath = path.join(__dirname, 'index.js');

// Argumentos para pasar al script principal
const args = process.argv.slice(2);

// Opciones para el proceso hijo
const options = {
  stdio: 'inherit', // Heredar stdin, stdout, stderr
  cwd: __dirname,
  env: process.env,
  windowsHide: false
};

// Ejecutar el script principal
const child = spawn(nodePath, [scriptPath, ...args], options);

// Manejar señales para terminar el proceso hijo
process.on('SIGINT', () => child.kill('SIGINT'));
process.on('SIGTERM', () => child.kill('SIGTERM'));
process.on('SIGBREAK', () => child.kill('SIGBREAK'));

// Manejar errores
child.on('error', (err) => {
  console.error(`Error al ejecutar el agente: ${err.message}`);
  process.exit(1);
});

// Manejar salida
child.on('exit', (code) => {
  process.exit(code);
});
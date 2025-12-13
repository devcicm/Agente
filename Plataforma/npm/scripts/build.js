#!/usr/bin/env node

/**
 * Script de compilaciИn para generar ejecutables nativos
 */

const { execSync } = require('child_process');
const { platform } = require('os');

console.log('=== CompilaciИn de Agente NPM ===\n');

try {
  // Instalar dependencias si es necesario
  console.log('Instalando dependencias...');
  execSync('npm install', { stdio: 'inherit' });

  // Determinar el sistema operativo
  const currentPlatform = platform();
  let targets = '';

  if (currentPlatform === 'win32') {
    targets = 'node18-win-x64';
    console.log('\nCompilando para Windows...');
  } else if (currentPlatform === 'linux') {
    targets = 'node18-linux-x64';
    console.log('\nCompilando para Linux...');
  } else if (currentPlatform === 'darwin') {
    targets = 'node18-macos-x64';
    console.log('\nCompilando para macOS...');
  } else {
    console.log(`\nPlataforma no soportada: ${currentPlatform}`);
    console.log('Compilando para todas las plataformas...');
    targets = 'node18-win-x64,node18-linux-x64,node18-macos-x64';
  }

  // Compilar con pkg
  const buildCommand = `pkg . --output agente-npm --targets ${targets}`;
  console.log(`Ejecutando: ${buildCommand}\n`);

  execSync(buildCommand, { stdio: 'inherit' });

  console.log('\n=== CompilaciИn completada ===');
  console.log('Ejecutables generados:');

  const isMultiTarget = targets.includes(',');

  if (isMultiTarget) {
    if (targets.includes('win')) {
      console.log('  - agente-npm-win.exe (Windows)');
    }
    if (targets.includes('linux')) {
      console.log('  - agente-npm-linux (Linux)');
    }
    if (targets.includes('macos')) {
      console.log('  - agente-npm-macos (macOS)');
    }
  } else {
    if (targets.includes('win')) {
      console.log('  - agente-npm.exe (Windows)');
    }
    if (targets.includes('linux')) {
      console.log('  - agente-npm (Linux)');
    }
    if (targets.includes('macos')) {
      console.log('  - agente-npm (macOS)');
    }
  }

  console.log('\nPara ejecutar:');
  if (isMultiTarget) {
    if (currentPlatform === 'win32') {
      console.log('  .\\agente-npm-win.exe');
    } else if (currentPlatform === 'linux') {
      console.log('  ./agente-npm-linux');
    } else if (currentPlatform === 'darwin') {
      console.log('  ./agente-npm-macos');
    } else {
      console.log('  ./agente-npm-<plataforma>');
    }
  } else {
    if (currentPlatform === 'win32') {
      console.log('  .\\agente-npm.exe');
    } else {
      console.log('  ./agente-npm');
    }
  }
} catch (error) {
  console.error('\nError durante la compilaciИn:');
  console.error(error.message);
  process.exit(1);
}


/**
 * Configuración específica para consola de sistema
 * Este archivo contiene configuraciones para cuando el agente
 * se ejecuta como un programa nativo en la consola del sistema
 */

const consoleConfig = {
  // Configuración de colores para consola de sistema
  // Cuando isSystemConsole = true, se desactivan los colores
  useColors: false,
  
  // Configuración de formato para consola de sistema
  // Usar formato simple sin caracteres especiales
  simpleFormat: true,
  
  // Configuración de rendimiento
  // Desactivar spinners y animaciones en consola de sistema
  useSpinners: false,
  
  // Configuración de compatibilidad
  // Asegurar compatibilidad con consolas antiguas
  legacyMode: true,
  
  // Configuración de salida
  // Usar salida estándar sin formato
  rawOutput: true
};

module.exports = {
  consoleConfig
};
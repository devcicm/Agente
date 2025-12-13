// Wrapper principal que redirige al agente en src/
// Mantiene compatibilidad con `node index.js` y `npm start`

try {
  module.exports = require('./src/agent/agent-with-logs.js');
} catch (error) {
  console.error('agent-with-logs.js no encontrado, usando versión alternativa');
  module.exports = require('./src/agent/agent-enhanced.js');
}


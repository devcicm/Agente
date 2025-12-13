// Interfaz gráfica para el agente usando Ink
const { render, Text, Box, useInput, useApp } = require('ink');
const React = require('react');

class AgentUI {
  constructor(logger) {
    this.logger = logger;
    this.messages = [];
    this.currentInput = '';
    this.isLoading = false;
  }
  
  addMessage(message, type = 'info') {
    this.messages.push({ text: message, type, timestamp: new Date() });
    if (this.messages.length > 100) {
      this.messages.shift(); // Mantener solo los últimos 100 mensajes
    }
  }
  
  setLoading(loading) {
    this.isLoading = loading;
  }
  
  clearMessages() {
    this.messages = [];
  }
  
  start() {
    const { exit } = render(
      React.createElement(App, {
        messages: this.messages,
        currentInput: this.currentInput,
        isLoading: this.isLoading,
        onInputChange: (input) => {
          this.currentInput = input;
        },
        onSubmit: (input) => {
          this.handleSubmit(input);
        }
      })
    );
    
    return exit;
  }
  
  handleSubmit(input) {
    if (input.trim()) {
      this.addMessage(`> ${input}`, 'user');
      // Aquí iría la lógica para procesar el input
      console.log('Input submitted:', input);
    }
  }
}

function App({ messages, currentInput, isLoading, onInputChange, onSubmit }) {
  const [input, setInput] = React.useState('');
  
  // Manejar entrada de usuario
  useInput((input, key) => {
    if (key.return) {
      onSubmit(input);
      setInput('');
    } else if (key.backspace || key.delete) {
      setInput(input.slice(0, -1));
    } else {
      setInput(input + key);
    }
  });
  
  return React.createElement(Box, { flexDirection: 'column' },
    // Header
    React.createElement(Box, { borderStyle: 'single', borderColor: 'blue', padding: 1 },
      React.createElement(Text, { color: 'blue', bold: true },
        ' Agente NPM - Interfaz Gráfica '
      )
    ),
    
    // Messages area
    React.createElement(Box, { flexDirection: 'column', flexGrow: 1, borderStyle: 'single', marginTop: 1, padding: 1 },
      messages.map((msg, index) => (
        React.createElement(Text, {
          key: index,
          color: msg.type === 'user' ? 'green' : msg.type === 'error' ? 'red' : 'white'
        }, msg.text)
      )),
      isLoading && React.createElement(Text, { color: 'yellow' }, 'Cargando...')
    ),
    
    // Input area
    React.createElement(Box, { marginTop: 1 },
      React.createElement(Text, { color: 'green' }, '> '),
      React.createElement(Text, null, input)
    ),
    
    // Status bar
    React.createElement(Box, { marginTop: 1, justifyContent: 'space-between' },
      React.createElement(Text, { color: 'gray' }, `Mensajes: ${messages.length}`),
      React.createElement(Text, { color: 'gray' }, `Estado: ${isLoading ? 'Ocupado' : 'Listo'}`)
    )
  );
}

module.exports = AgentUI;
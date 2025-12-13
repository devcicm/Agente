'use strict';

const chalk = require('chalk');
const { marked } = require('marked');
const codeFormatter = require('./code');

/**
 * Renderizador de Markdown para terminal con estilo Claude Code
 */
class TerminalMarkdownRenderer {
  constructor(options = {}) {
    this.options = {
      width: options.width || 80,
      indent: options.indent || 0,
      theme: options.theme || 'default',
      ...options
    };

    this.indentString = ' '.repeat(this.options.indent);
  }

  /**
   * Renderiza texto markdown a formato terminal
   */
  render(markdown) {
    if (!markdown || typeof markdown !== 'string') {
      return '';
    }

    try {
      const tokens = marked.lexer(markdown);
      return this.renderTokens(tokens);
    } catch (error) {
      // Fallback a texto plano si falla el parsing
      return this.indentString + markdown;
    }
  }

  /**
   * Renderiza array de tokens
   */
  renderTokens(tokens) {
    const output = [];

    for (let i = 0; i < tokens.length; i++) {
      const token = tokens[i];
      const rendered = this.renderToken(token);
      if (rendered) {
        output.push(rendered);
      }
    }

    return output.join('\n');
  }

  /**
   * Renderiza un token individual
   */
  renderToken(token) {
    switch (token.type) {
      case 'heading':
        return this.renderHeading(token);
      case 'paragraph':
        return this.renderParagraph(token);
      case 'code':
        return this.renderCode(token);
      case 'blockquote':
        return this.renderBlockquote(token);
      case 'list':
        return this.renderList(token);
      case 'table':
        return this.renderTable(token);
      case 'hr':
        return this.renderHr();
      case 'space':
        return '';
      default:
        // Para tipos no manejados, intentar renderizar el texto crudo
        if (token.text) {
          return this.indentString + this.renderInline(token.text);
        }
        return '';
    }
  }

  /**
   * Renderiza encabezados con estilo
   */
  renderHeading(token) {
    const text = this.renderInline(token.text);
    const indent = this.indentString;

    switch (token.depth) {
      case 1:
        return `\n${indent}${chalk.bold.cyan(text)}\n${indent}${'═'.repeat(Math.min(text.length, 80))}`;
      case 2:
        return `\n${indent}${chalk.bold.blue(text)}\n${indent}${'─'.repeat(Math.min(text.length, 80))}`;
      case 3:
        return `\n${indent}${chalk.bold.yellow(text)}`;
      case 4:
        return `\n${indent}${chalk.bold.white(text)}`;
      default:
        return `\n${indent}${chalk.bold(text)}`;
    }
  }

  /**
   * Renderiza párrafos con word wrap
   */
  renderParagraph(token) {
    const text = this.renderInline(token.text);
    return this.wrapText(text, this.options.width - this.options.indent);
  }

  /**
   * Renderiza bloques de código con syntax highlighting
   */
  renderCode(token) {
    const lang = token.lang || 'text';
    const code = token.text;

    return codeFormatter.formatCodeBlock(code, lang, {
      indent: this.options.indent,
      showLanguage: true
    });
  }

  /**
   * Renderiza blockquotes
   */
  renderBlockquote(token) {
    const lines = token.text.split('\n');
    const indent = this.indentString;

    return lines.map(line => {
      return `${indent}${chalk.gray('│')} ${chalk.italic.gray(line)}`;
    }).join('\n');
  }

  /**
   * Renderiza listas (ordenadas y no ordenadas)
   */
  renderList(token) {
    const indent = this.indentString;
    const items = [];

    token.items.forEach((item, index) => {
      // Determinar el marcador
      let marker;
      if (item.task !== undefined) {
        // Lista de tareas (checkbox)
        marker = item.checked
          ? chalk.green('[✓]')
          : chalk.gray('[ ]');
      } else if (token.ordered) {
        // Lista ordenada (números)
        marker = chalk.cyan(`${index + 1}.`);
      } else {
        // Lista no ordenada (bullets)
        marker = chalk.cyan('•');
      }

      const text = this.renderInline(item.text);
      const wrapped = this.wrapText(text, this.options.width - this.options.indent - 3);
      const lines = wrapped.split('\n');

      // Primera línea con marker
      items.push(`${indent}${marker} ${lines[0]}`);

      // Líneas subsecuentes indentadas
      for (let i = 1; i < lines.length; i++) {
        items.push(`${indent}  ${lines[i]}`);
      }
    });

    return items.join('\n');
  }

  /**
   * Renderiza tablas
   */
  renderTable(token) {
    const Table = require('cli-table3');

    const headers = token.header.map(cell => {
      return chalk.cyan.bold(this.renderInline(cell.text));
    });

    const table = new Table({
      head: headers,
      style: {
        head: [],
        border: ['cyan'],
        'padding-left': 1,
        'padding-right': 1
      },
      wordWrap: true,
      colWidths: headers.map(() => null) // Auto-width
    });

    token.rows.forEach(row => {
      const cells = row.map(cell => this.renderInline(cell.text));
      table.push(cells);
    });

    const tableStr = table.toString();
    const indent = this.indentString;

    return tableStr.split('\n').map(line => indent + line).join('\n');
  }

  /**
   * Renderiza líneas horizontales
   */
  renderHr() {
    const width = Math.min(80, this.options.width - this.options.indent);
    return this.indentString + chalk.gray('─'.repeat(width));
  }

  /**
   * Renderiza elementos inline (negrita, cursiva, código inline, links)
   */
  renderInline(text) {
    if (!text || typeof text !== 'string') {
      return '';
    }

    // Procesar tokens inline manualmente para evitar dependencias complejas
    let result = text;

    // Decodificar HTML entities
    result = result
      .replace(/&quot;/g, '"')
      .replace(/&#x27;/g, "'")
      .replace(/&amp;/g, '&')
      .replace(/&lt;/g, '<')
      .replace(/&gt;/g, '>')
      .replace(/&#(\d+);/g, (match, dec) => String.fromCharCode(dec))
      .replace(/&#x([0-9a-fA-F]+);/g, (match, hex) => String.fromCharCode(parseInt(hex, 16)));

    // Código inline `code`
    result = result.replace(/`([^`]+)`/g, (match, code) => {
      return chalk.bgGray.white(` ${code} `);
    });

    // Negrita **text** o __text__
    result = result.replace(/\*\*(.+?)\*\*/g, (match, text) => {
      return chalk.bold(text);
    });
    result = result.replace(/__(.+?)__/g, (match, text) => {
      return chalk.bold(text);
    });

    // Cursiva *text* o _text_
    result = result.replace(/\*(.+?)\*/g, (match, text) => {
      return chalk.italic(text);
    });
    result = result.replace(/_(.+?)_/g, (match, text) => {
      return chalk.italic(text);
    });

    // Links [text](url)
    result = result.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (match, text, url) => {
      return `${chalk.blue.underline(text)} ${chalk.gray(`(${url})`)}`;
    });

    return result;
  }

  /**
   * Word wrap con respeto por ANSI codes
   */
  wrapText(text, width) {
    const stripAnsi = require('strip-ansi');

    if (stripAnsi(text).length <= width) {
      return this.indentString + text;
    }

    const words = text.split(' ');
    const lines = [];
    let currentLine = '';

    for (const word of words) {
      const testLine = currentLine ? `${currentLine} ${word}` : word;
      const plainTest = stripAnsi(testLine);

      if (plainTest.length <= width) {
        currentLine = testLine;
      } else {
        if (currentLine) {
          lines.push(this.indentString + currentLine);
        }
        currentLine = word;
      }
    }

    if (currentLine) {
      lines.push(this.indentString + currentLine);
    }

    return lines.join('\n');
  }
}

/**
 * Función de conveniencia para renderizar markdown
 */
function renderMarkdown(markdown, options = {}) {
  const renderer = new TerminalMarkdownRenderer(options);
  return renderer.render(markdown);
}

module.exports = {
  TerminalMarkdownRenderer,
  renderMarkdown
};

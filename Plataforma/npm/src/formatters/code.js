'use strict';

const chalk = require('chalk');
const hljs = require('highlight.js');

/**
 * Mapa de colores ANSI para diferentes tipos de tokens de highlight.js
 */
const THEME = {
  keyword: chalk.magenta,
  built_in: chalk.cyan,
  type: chalk.cyan.bold,
  literal: chalk.blue,
  number: chalk.green,
  string: chalk.green,
  regexp: chalk.red,
  comment: chalk.gray.italic,
  doctag: chalk.gray.bold,
  meta: chalk.yellow,
  'meta-keyword': chalk.yellow.bold,
  'meta-string': chalk.yellow,
  function: chalk.yellow,
  title: chalk.yellow.bold,
  params: chalk.white,
  variable: chalk.blue,
  'template-variable': chalk.blue,
  'template-tag': chalk.magenta,
  addition: chalk.green,
  deletion: chalk.red,
  link: chalk.cyan.underline,
  symbol: chalk.cyan,
  bullet: chalk.blue,
  attribute: chalk.yellow,
  section: chalk.yellow.bold,
  name: chalk.blue,
  tag: chalk.blue,
  selector: chalk.magenta,
  'selector-tag': chalk.magenta,
  'selector-id': chalk.blue.bold,
  'selector-class': chalk.blue,
  'selector-attr': chalk.cyan,
  'selector-pseudo': chalk.cyan,
  operator: chalk.white,
  punctuation: chalk.gray,
  subst: chalk.white
};

/**
 * Convierte tokens de highlight.js a texto con colores ANSI
 */
function highlightTokens(code, language) {
  try {
    const result = language && language !== 'text'
      ? hljs.highlight(code, { language, ignoreIllegals: true })
      : { value: code, language: 'text' };

    // Convertir HTML-like tokens a ANSI
    let output = result.value;

    // highlight.js usa <span class="hljs-keyword"> etc
    // Vamos a procesar estos tokens
    const tokenRegex = /<span class="hljs-([^"]+)">([^<]*)<\/span>/g;

    output = output.replace(tokenRegex, (match, className, text) => {
      const colorFn = THEME[className] || chalk.white;
      return colorFn(text);
    });

    // Limpiar cualquier tag HTML restante
    output = output.replace(/<[^>]+>/g, '');

    return {
      highlighted: output,
      language: result.language || language || 'text'
    };
  } catch (error) {
    // Si falla el highlighting, retornar texto plano
    return {
      highlighted: code,
      language: 'text'
    };
  }
}

/**
 * Formatea un bloque de código con borde y syntax highlighting
 */
function formatCodeBlock(code, language = 'text', options = {}) {
  const {
    indent = 0,
    showLanguage = true,
    showLineNumbers = false,
    borderColor = 'cyan',
    maxWidth = 120
  } = options;

  const indentStr = ' '.repeat(indent);
  const { highlighted, language: detectedLang } = highlightTokens(code, language);

  const lines = highlighted.split('\n');
  const maxLineLength = Math.max(...lines.map(line => {
    const stripAnsi = require('strip-ansi');
    return stripAnsi(line).length;
  }));

  const boxWidth = Math.min(maxWidth, maxLineLength + 4);
  const borderChar = '─';
  const cornerTL = '┌';
  const cornerTR = '┐';
  const cornerBL = '└';
  const cornerBR = '┘';
  const vertical = '│';

  const colorFn = chalk[borderColor] || chalk.cyan;

  const output = [];

  // Línea superior
  const langLabel = showLanguage && detectedLang !== 'text' ? ` ${detectedLang} ` : '';
  const topBorder = cornerTL + borderChar.repeat(boxWidth - 2 - langLabel.length) + langLabel + cornerTR;
  output.push(indentStr + colorFn(topBorder));

  // Líneas de código
  lines.forEach((line, index) => {
    const lineNum = showLineNumbers ? chalk.gray(`${(index + 1).toString().padStart(3)} `) : '';
    const paddedLine = ` ${line} `;
    output.push(indentStr + colorFn(vertical) + lineNum + paddedLine);
  });

  // Línea inferior
  const bottomBorder = cornerBL + borderChar.repeat(boxWidth - 2) + cornerBR;
  output.push(indentStr + colorFn(bottomBorder));

  return output.join('\n');
}

/**
 * Formatea código inline (sin bordes)
 */
function formatInlineCode(code, language = 'text') {
  const { highlighted } = highlightTokens(code, language);
  return chalk.bgGray.white(` ${highlighted} `);
}

/**
 * Lista lenguajes soportados por highlight.js
 */
function getSupportedLanguages() {
  return hljs.listLanguages();
}

/**
 * Detecta automáticamente el lenguaje del código
 */
function detectLanguage(code) {
  try {
    const result = hljs.highlightAuto(code);
    return result.language || 'text';
  } catch {
    return 'text';
  }
}

module.exports = {
  highlightTokens,
  formatCodeBlock,
  formatInlineCode,
  getSupportedLanguages,
  detectLanguage,
  THEME
};

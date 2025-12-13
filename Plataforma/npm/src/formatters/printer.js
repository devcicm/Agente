'use strict';

const chalk = require('chalk');
const boxen = require('boxen');
const { renderMarkdown } = require('./markdown');
const codeFormatter = require('./code');

/**
 * Imprime una línea horizontal decorativa
 */
function printHr(char = '═', width = 80, color = 'cyan') {
  const colorFn = chalk[color] || chalk.cyan;
  console.log(colorFn(char.repeat(width)));
}

/**
 * Imprime un encabezado con caja
 */
function printHeader(text, options = {}) {
  const {
    color = 'cyan',
    padding = 1,
    margin = 1,
    borderStyle = 'round'
  } = options;

  console.log(boxen(chalk.bold(text), {
    padding,
    margin,
    borderColor: color,
    borderStyle
  }));
}

/**
 * Imprime una sección con título
 */
function printSection(title, content, options = {}) {
  const {
    titleColor = 'cyan',
    contentColor = 'white',
    indent = 0,
    markdown = false
  } = options;

  const indentStr = ' '.repeat(indent);
  const colorTitle = chalk[titleColor] || chalk.cyan;
  const colorContent = chalk[contentColor] || chalk.white;

  console.log(`\n${indentStr}${colorTitle.bold(`[${title}]`)}`);

  if (markdown) {
    const rendered = renderMarkdown(content, { indent });
    console.log(rendered);
  } else {
    content.split('\n').forEach(line => {
      console.log(`${indentStr}${colorContent(line)}`);
    });
  }
}

/**
 * Imprime metadata en formato clave-valor
 */
function printMetadata(data, options = {}) {
  const {
    indent = 0,
    keyColor = 'gray',
    valueColor = 'white'
  } = options;

  const indentStr = ' '.repeat(indent);
  const colorKey = chalk[keyColor] || chalk.gray;
  const colorValue = chalk[valueColor] || chalk.white;

  Object.entries(data).forEach(([key, value]) => {
    const displayValue = typeof value === 'object'
      ? JSON.stringify(value, null, 2)
      : String(value);

    console.log(`${indentStr}${colorKey(key)}: ${colorValue(displayValue)}`);
  });
}

/**
 * Imprime resultado de LLM con formato estilo Claude Code
 */
function printLLMResult(result, options = {}) {
  const {
    showThinking = true,
    showUsage = true,
    showRaw = false,
    markdownRender = true,
    showModel = true,
    showId = true
  } = options;

  if (!result) {
    console.log(chalk.red('❌ No hay resultado para mostrar'));
    return;
  }

  // Separador inicial
  printHr('═', 80, 'cyan');

  // Encabezado con modelo
  if (showModel && result.model) {
    console.log(chalk.cyan.bold(`\n${result.model}`));
  }

  if (showId && result.id) {
    console.log(chalk.gray(`ID: ${result.id}`));
  }

  // Thinking/Reasoning
  if (showThinking && result.thinking) {
    printSection('PENSAMIENTO', result.thinking, {
      titleColor: 'yellow',
      contentColor: 'gray',
      indent: 0,
      markdown: markdownRender
    });
  }

  // Respuesta principal
  if (result.response) {
    printSection('RESPUESTA', result.response, {
      titleColor: 'green',
      contentColor: 'white',
      indent: 0,
      markdown: markdownRender
    });
  } else {
    console.log(chalk.yellow('\n⚠️ La respuesta está vacía'));
  }

  // Usage/Metadata
  if (showUsage && result.usage) {
    printSection('USO', '', {
      titleColor: 'blue',
      indent: 0
    });

    const usageData = {
      'Tokens entrada': result.usage.input_tokens || result.usage.prompt_tokens || 'N/A',
      'Tokens salida': result.usage.output_tokens || result.usage.completion_tokens || 'N/A',
      'Total tokens': result.usage.total_tokens || 'N/A'
    };

    printMetadata(usageData, {
      indent: 2,
      keyColor: 'gray',
      valueColor: 'cyan'
    });
  }

  // Previous response ID
  if (result.previous_response_id !== undefined) {
    console.log(chalk.gray(`\nprevious_response_id: ${result.previous_response_id || 'null'}`));
  }

  // Raw JSON
  if (showRaw && result.raw) {
    printSection('RAW JSON', JSON.stringify(result.raw, null, 2), {
      titleColor: 'magenta',
      contentColor: 'gray',
      indent: 0,
      markdown: false
    });
  }

  // Separador final
  printHr('═', 80, 'cyan');
  console.log();
}

/**
 * Imprime resultado de comparación de modelos
 */
function printComparisonResults(results, prompt, options = {}) {
  const { markdown = true } = options;

  printHeader(`Comparación de Modelos: "${prompt}"`, {
    color: 'cyan',
    borderStyle: 'double'
  });

  results.forEach((result, index) => {
    console.log(chalk.cyan.bold(`\n[${index + 1}/${results.length}] ${result.model}`));

    if (result.error) {
      console.log(chalk.red(`❌ Error: ${result.error}`));
      console.log(chalk.gray(`Tiempo: ${result.duration_ms}ms`));
    } else {
      const parsed = result.parsed || {};

      if (parsed.response) {
        if (markdown) {
          console.log(renderMarkdown(parsed.response, { indent: 2 }));
        } else {
          console.log(`  ${parsed.response}`);
        }
      }

      console.log(chalk.gray(`\n  Tiempo: ${result.duration_ms}ms`));

      if (parsed.usage) {
        console.log(chalk.gray(`  Tokens: ${parsed.usage.input_tokens || 0} in / ${parsed.usage.output_tokens || 0} out`));
      }
    }

    if (index < results.length - 1) {
      printHr('─', 80, 'gray');
    }
  });

  printHr('═', 80, 'cyan');
  console.log();
}

/**
 * Imprime mensaje de error
 */
function printError(message, details = null) {
  console.log(chalk.red.bold(`\n❌ Error: ${message}`));

  if (details) {
    console.log(chalk.red(details));
  }

  console.log();
}

/**
 * Imprime mensaje de éxito
 */
function printSuccess(message) {
  console.log(chalk.green.bold(`\n✅ ${message}`));
  console.log();
}

/**
 * Imprime mensaje de advertencia
 */
function printWarning(message) {
  console.log(chalk.yellow.bold(`\n⚠️ ${message}`));
  console.log();
}

/**
 * Imprime mensaje informativo
 */
function printInfo(message) {
  console.log(chalk.blue.bold(`\nℹ️ ${message}`));
  console.log();
}

/**
 * Imprime una tabla de información
 */
function printTable(headers, rows, options = {}) {
  const Table = require('cli-table3');
  const {
    headColor = 'cyan',
    borderColor = 'cyan'
  } = options;

  const colorHead = chalk[headColor] || chalk.cyan;

  const table = new Table({
    head: headers.map(h => colorHead.bold(h)),
    style: {
      head: [],
      border: [borderColor],
      'padding-left': 1,
      'padding-right': 1
    },
    wordWrap: true
  });

  rows.forEach(row => table.push(row));

  console.log(table.toString());
  console.log();
}

/**
 * Imprime lista de modelos
 */
function printModelsList(models, currentModel = null) {
  printHeader('Modelos Disponibles', {
    color: 'cyan',
    borderStyle: 'round'
  });

  models.forEach((model, index) => {
    const modelId = model.id || model;
    const isCurrent = modelId === currentModel;
    const marker = isCurrent ? chalk.green('→') : ' ';
    const displayName = isCurrent ? chalk.green.bold(modelId) : chalk.white(modelId);

    console.log(`${marker} ${index + 1}. ${displayName}`);

    if (model.name && model.name !== modelId) {
      console.log(`     ${chalk.gray(model.name)}`);
    }
  });

  console.log();
}

/**
 * Imprime el menú de comandos interactivo
 */
function printCommandMenu(commands) {
  printHeader('Comandos Disponibles', {
    color: 'blue',
    borderStyle: 'round'
  });

  commands.forEach((cmd, index) => {
    console.log(`  ${chalk.cyan.bold(`${index + 1}.`)} ${chalk.yellow(cmd.cmd.padEnd(15))} ${chalk.gray('─')} ${chalk.white(cmd.desc)}`);
  });

  console.log();
}

/**
 * Imprime el banner de bienvenida
 */
function printWelcomeBanner(config) {
  const banner = `
╔═══════════════════════════════════════════════════════════╗
║                                                           ║
║          AGENTE NPM - Modo Interactivo Mejorado          ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝
`;

  console.log(chalk.cyan(banner));

  const configInfo = {
    'Modelo': config.model,
    'Endpoint': config.baseUrl,
    'Modo': config.stream ? 'Streaming' : 'Batch',
    'Debug': config.debug ? 'Activado' : 'Desactivado',
    'Markdown': 'Activado (estilo Claude Code)'
  };

  printMetadata(configInfo, {
    indent: 2,
    keyColor: 'cyan',
    valueColor: 'white'
  });

  console.log();
}

module.exports = {
  printHr,
  printHeader,
  printSection,
  printMetadata,
  printLLMResult,
  printComparisonResults,
  printError,
  printSuccess,
  printWarning,
  printInfo,
  printTable,
  printModelsList,
  printCommandMenu,
  printWelcomeBanner
};

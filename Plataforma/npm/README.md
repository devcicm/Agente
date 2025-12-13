# Agente NPM - Interfaz Node.js para LLM en Consola de Sistema ƒo.

**ƒo. AGENTE FUNCIONAL CREADO CON Ç%XITO**

Este es un agente CLI basado en Node.js para interactuar con modelos de lenguaje local usando LM Studio. Proporciona una interfaz funcional que se ejecuta directamente en la consola del sistema operativo (cmd.exe, bash, etc.) como un programa nativo.

**CaracterÇðstica principal**: El agente estÇ­ diseÇñado para ejecutarse como un programa de consola de sistema, no como una aplicaciÇün web. Se integra perfectamente con la consola del sistema operativo.

## ÐYZ% Estado Actual: COMPLETADO

ƒo. **Agente funcional creado y probado**
ƒo. **Ejecutable nativo generado: `agente-npm.exe`**
ƒo. **TamaÇño del ejecutable: 38.2 MB**
ƒo. **Compatibilidad: Windows (versiones para Linux/Mac disponibles)**
ƒo. **IntegraciÇün con consola de sistema: COMPLETADA**

## CaracterÇðsticas

- **Interfaz CLI moderna**: Usa Commander para manejo de comandos
- **Modo interactivo**: Consola con prompts y colores
- **Soporte para streaming**: Respuestas en tiempo real
- **Modo debug**: InformaciÇün detallada para desarrollo
- **GestiÇün de modelos**: Cambio dinÇ­mico de modelos
- **ConfiguraciÇün flexible**: Usa variables de entorno

## Requisitos

- Node.js 18+
- LM Studio instalado y ejecutÇ­ndose
- Modelo LLM cargado en LM Studio

## InstalaciÇün

```bash
cd Plataforma/npm
npm install
```

## ConfiguraciÇün

Cree un archivo `.env` con:

```env
LMSTUDIO_URL=http://localhost:1234
LMSTUDIO_MODEL=gpt-oss-20b-gpt-5-reasoning-distill
DEBUG_MODE=false
STREAM_MODE=false
```

## Uso

### Modo Consola de Sistema (Recomendado)

El agente estÇ­ diseÇñado para ejecutarse como un programa nativo en la consola del sistema:

```bash
# En Windows (cmd.exe)
agente-npm

# En Linux/Mac (bash)
./agente-npm

# Con logs mejorados
npm run logs
```

### Modo interactivo con Node.js

```bash
npm start
```

### Commands especÇðficos

```bash
# Probar conexiÇün
npm start test

# Listar modelos disponibles
npm start models

# Cambiar modelo
npm start model <model-id>

# Activar streaming
npm start stream

# Desactivar streaming
npm start nostream

# Activar debug
npm start debug

# Desactivar debug
npm start nodebug

# Enviar prompt directamente
npm start "¶¨CuÇ­l es la capital de Francia?"
```

### Commands en modo interactivo

- `/exit` - Salir del programa
- `/clear` - Limpiar pantalla
- `/help` - Mostrar lista de comandos
- `/models` - Listar modelos disponibles
- `/model <id>` - Cambiar modelo activo
- `/origins <ip:puerto|url>` - Cambiar origen del LLM
- `/stream` - Activar modo streaming
- `/nostream` - Desactivar modo streaming
- `/debug` - Activar modo debug
- `/nodebug` - Desactivar modo debug
- `/test` - Probar conexiÇün con LM Studio
- `/ping` - Hacer ping a LM Studio

## Arquitectura

El agente usa las siguientes tecnologÇðas:

- **Axios**: Para comunicaciÇün HTTP con LM Studio
- **Commander**: Para manejo de comandos CLI
- **Dotenv**: Para configuraciÇün mediante variables de entorno
- **Readline**: Para interfaz interactiva
- **Wrapper de consola**: Para ejecuciÇün como programa nativo
- **Logger personalizado**: Para sistema de logs mejorado

### Sistema de Logs

El agente incluye un sistema de logs mejorado con:
- **Logs estructurados** con timestamps
- **Niveles de log** (INFO, WARN, ERROR, DEBUG)
- **Persistencia en archivo** (`logs/agente.log`)
- **Manejo de cola asÇðncrono** para evitar bloqueos
- **RotaciÇün automÇ­tica** para controlar tamaÇño
- **Emojis y formato claro** en consola

### Modo Consola de Sistema

Cuando se ejecuta como programa compilado:
- Sin colores (para compatibilidad con consolas antiguas)
- Sin spinners (para mejor rendimiento)
- Interfaz de sÇðmbolo de sistema pura
- Compatible con scripts batch y shell

### Modo Node.js

Cuando se ejecuta con `node index.js`:
- Con colores y formato mejorado
- Con spinners de carga
- Ideal para desarrollo

## IntegraciÇün con el proyecto existente

Este agente NPM complementa el Engine LLM existente en C# (`Plataforma/C#/engine`) proporcionando:

1. **Alternativa moderna**: Usa el ecosistema Node.js
2. **Mismo backend**: Se conecta al mismo LM Studio
3. **Interfaz mejorada**: Colores, spinners y UX moderna
4. **Extensibilidad**: FÇ­cil de modificar y extender

## EjecuciÇün RÇ­pida

### Usando scripts preconfigurados

**Windows:**
```batch
scripts/run-agent.bat
```

**Linux/Mac:**
```bash
chmod +x scripts/run-agent.sh
./scripts/run-agent.sh
```

## CompilaciÇün como Ejecutable Nativo

Para crear un ejecutable independiente que funcione como un programa de consola de sistema:

```bash
# Instalar pkg globalmente
npm install -g pkg

# Compilar para Windows, Linux y Mac
npm run build
```

Esto generará ejecutables en la carpeta actual:
- `agente-npm.exe` para Windows
- `agente-npm` para Linux
- `agente-npm` para MacOS
Si compilas varias plataformas a la vez, `pkg` agrega sufijos como `agente-npm-linux`.

### Ventajas de la compilaciÇün:

1. **Sin dependencias**: El ejecutable incluye Node.js y todas las dependencias
2. **Portabilidad**: Puede ejecutarse en cualquier sistema sin instalar Node.js
3. **IntegraciÇün**: Se comporta como un programa nativo de consola
4. **Rendimiento**: Optimizado para ejecuciÇün en consola de sistema

## Desarrollo

Para desarrollo con recarga automÇ­tica:

```bash
npm run dev
```

## ConfiguraciÇün avanzada

Puede configurar mÇ§ltiples modelos y cambiar entre ellos dinÇ­micamente usando los commands `/model` o `npm start model <id>`.

## Notas

- AsegÇ§rese de que LM Studio estÇ¸ ejecutÇ­ndose antes de usar el agente
- El agente usa la API `/v1/responses` de LM Studio
- Soporte para streaming mediante Server-Sent Events (SSE)
- Manejo de errores con informaciÇün detallada en modo debug


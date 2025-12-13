# Notas de Compilación del Ejecutable

## Versión Actual

**Fecha:** 2025-12-13
**Versión:** 1.2.0
**Ejecutable:** `agente-npm.exe`
**Tamaño:** 63 MB
**Plataforma:** Windows x64 (Node.js 18)

## Cambios en esta Build

### UI Mejorada con Markdown (v1.2.0)

Esta compilación incluye el nuevo sistema de renderizado de markdown estilo Claude Code:

✅ **Nuevas características:**
- Renderizado completo de markdown en respuestas
- Syntax highlighting de código (180+ lenguajes)
- Tablas formateadas con bordes
- Banner de bienvenida mejorado con cajas decorativas
- Lista de modelos con formato visual
- Menú de comandos estilizado
- Mensajes de estado con iconos y colores
- Comparación de modelos con output formateado

✅ **Nuevas dependencias incluidas:**
- chalk@4.1.2 - Colores en terminal
- marked@4.3.0 - Parser de markdown
- highlight.js@11.9.0 - Syntax highlighting
- cli-table3@0.6.3 - Tablas formateadas
- boxen@5.1.2 - Cajas decorativas
- strip-ansi@6.0.1 - Limpieza de ANSI codes
- terminal-link@2.1.1 - Links en terminal

✅ **Nuevos módulos:**
- `src/formatters/markdown.js` - Renderizador de markdown
- `src/formatters/code.js` - Syntax highlighter
- `src/formatters/printer.js` - Funciones de impresión mejoradas

### Comparación con Versión Anterior

| Aspecto | v1.0.0 (Anterior) | v1.2.0 (Actual) |
|---------|-------------------|-----------------|
| Tamaño | 60 MB | 63 MB |
| Markdown | ❌ No | ✅ Sí |
| Syntax Highlighting | ❌ No | ✅ Sí (180+ lenguajes) |
| Tablas | ❌ No | ✅ Sí |
| UI Decorativa | ⚠️ Básica | ✅ Avanzada |
| Dependencias UI | 0 | 7 |

## Proceso de Compilación

### Comando

```bash
npm run build
```

Este comando ejecuta `scripts/build.js` que:
1. Instala/verifica dependencias con `npm install`
2. Ejecuta `pkg . --output agente-npm --targets node18-win-x64`
3. Genera el ejecutable `agente-npm.exe`

### Configuración de pkg

La configuración está en `package.json`:

```json
{
  "bin": "index.js",
  "pkg": {
    "targets": ["node18-win-x64"],
    "outputPath": ".",
    "assets": [
      "node_modules/highlight.js/styles/**/*"
    ]
  }
}
```

### Tiempo de Compilación

- **Duración típica:** 20-30 segundos
- **Requiere:** Internet (si hay nuevas dependencias)
- **Salida:** `agente-npm.exe` en el directorio raíz del proyecto

## Ejecución del Ejecutable

### Windows

```cmd
# Modo interactivo
.\agente-npm.exe

# Single-shot
.\agente-npm.exe "tu pregunta aquí"

# Comandos específicos
.\agente-npm.exe models
.\agente-npm.exe test
.\agente-npm.exe compare "pregunta"
```

### Verificar Versión

```cmd
.\agente-npm.exe --version
```

Salida esperada: `1.2.0`

### Ayuda

```cmd
.\agente-npm.exe --help
```

## Requisitos de Ejecución

### Sistema Operativo
- ✅ Windows 10/11 (x64)
- ✅ Windows Server 2016+ (x64)
- ❌ Windows 7/8 (no soportado)
- ❌ Windows 32-bit (no incluido en esta build)

### Terminal Recomendada
- ✅ **Windows Terminal** (Recomendado) - Soporte completo Unicode y colores
- ✅ VSCode Terminal integrada
- ⚠️ PowerShell 5.1/7+ - Funciona, pero requiere configurar encoding
- ⚠️ CMD.exe - Funciona con limitaciones visuales

### Configuración de Terminal

Para mejor experiencia visual:

```powershell
# PowerShell - Configurar UTF-8
chcp 65001
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
```

```cmd
# CMD.exe - Configurar UTF-8
chcp 65001
```

### LM Studio

El ejecutable **requiere** que LM Studio esté ejecutándose:

- ✅ LM Studio instalado y ejecutándose
- ✅ Servidor local activo en `http://localhost:1234`
- ✅ Al menos un modelo LLM cargado

## Testing del Ejecutable

### Test Básico de Funcionamiento

```cmd
# 1. Verificar versión
.\agente-npm.exe --version

# 2. Probar conexión (requiere LM Studio)
.\agente-npm.exe test

# 3. Listar modelos
.\agente-npm.exe models
```

### Test de UI con Markdown

```cmd
# Pregunta que debería generar markdown rico
.\agente-npm.exe "Explica qué es JavaScript con ejemplos de código"
```

Deberías ver:
- ✅ Banner decorativo
- ✅ Encabezados con subrayados
- ✅ Bloques de código con syntax highlighting
- ✅ Listas con bullets coloridos
- ✅ Separadores visuales

### Test Interactivo

```cmd
.\agente-npm.exe
```

Deberías ver:
```
╔═══════════════════════════════════════════════════════════╗
║                                                           ║
║          AGENTE NPM - Modo Interactivo Mejorado          ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝

  Modelo: deepseek/deepseek-r1-0528-qwen3-8b
  Endpoint: http://localhost:1234
  Modo: Batch
  Debug: Desactivado
  Markdown: Activado (estilo Claude Code)

   ╭──────────────────────────╮
   │                          │
   │   Comandos Disponibles   │
   │                          │
   ╰──────────────────────────╯

  1. /help           ─ Mostrar ayuda
  ...
```

## Troubleshooting

### Ejecutable no inicia

**Error:** "No se reconoce como comando..."
- **Causa:** Path incorrecto
- **Solución:** Navegar al directorio del ejecutable primero

**Error:** "This app can't run on your PC"
- **Causa:** Versión de Windows no compatible o arquitectura incorrecta
- **Solución:** Verificar Windows 10+ (x64)

### UI se ve mal

**Problema:** Caracteres extraños en lugar de cajas/bordes
- **Causa:** Codepage no UTF-8
- **Solución:** Ejecutar `chcp 65001` antes de iniciar

**Problema:** Sin colores
- **Causa:** Terminal antigua sin soporte ANSI
- **Solución:** Usar Windows Terminal o VSCode Terminal

### No conecta a LM Studio

**Error:** "ECONNREFUSED 127.0.0.1:1234"
- **Causa:** LM Studio no está ejecutándose
- **Solución:** Iniciar LM Studio y cargar un modelo

**Error:** "No hay modelos disponibles"
- **Causa:** No hay modelos cargados en LM Studio
- **Solución:** Cargar al menos un modelo en LM Studio

## Distribución

### Archivo a Distribuir

```
agente-npm.exe  (63 MB)
```

Este archivo es **standalone** - no requiere Node.js instalado en la máquina objetivo.

### Requisitos para el Usuario Final

1. Windows 10/11 (x64)
2. LM Studio instalado y ejecutándose
3. Modelo LLM cargado en LM Studio
4. Terminal moderna (Windows Terminal recomendado)

### No Incluido en el Ejecutable

- ❌ LM Studio
- ❌ Modelos LLM
- ❌ Archivo .env (se debe configurar o usar defaults)

### Instrucciones para Usuario Final

```markdown
1. Descargar agente-npm.exe
2. Colocarlo en un directorio conveniente (ej: C:\Tools\)
3. Iniciar LM Studio y cargar un modelo
4. Abrir Windows Terminal
5. Navegar al directorio: cd C:\Tools
6. Ejecutar: .\agente-npm.exe
```

## Versionado

### Historial de Versiones

- **v1.2.0** (2025-12-13): UI con markdown estilo Claude Code
- **v1.0.0** (2025-12-12): Versión inicial con UI básica

### Próximas Versiones

Planeado para futuras releases:
- v1.3.0: Temas de colores personalizables
- v1.4.0: Paginación para respuestas largas
- v1.5.0: Exportar respuestas a HTML/PDF

## Mantenimiento

### Recompilar después de Cambios

Si modificas el código fuente, debes recompilar:

```bash
# Eliminar ejecutable antiguo
rm agente-npm.exe

# Recompilar
npm run build

# Verificar nueva versión
.\agente-npm.exe --version
```

### Actualizar Dependencias

```bash
# Actualizar a versiones más recientes
npm update

# Recompilar
npm run build
```

### Testing antes de Distribución

Checklist antes de distribuir nueva build:

- [ ] Compilación exitosa sin errores
- [ ] Versión actualizada en package.json
- [ ] `.\agente-npm.exe --version` muestra versión correcta
- [ ] `.\agente-npm.exe test` conecta a LM Studio
- [ ] `.\agente-npm.exe models` lista modelos correctamente
- [ ] Modo interactivo muestra banner y UI mejorada
- [ ] Respuestas muestran markdown renderizado
- [ ] Syntax highlighting funciona en bloques de código
- [ ] Tablas se renderizan correctamente
- [ ] Sin warnings críticos durante ejecución

---

**Última actualización:** 2025-12-13
**Compilado por:** Claude Sonnet 4.5
**Documentación:** Completa

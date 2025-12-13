# Agente LLM Autónomo - Resumen Ejecutivo

## 🎯 ¿Qué es esto?

Un agente LLM que puede **ejecutar acciones** en tu sistema de forma autónoma:

- ✅ Ejecutar comandos de terminal
- ✅ Leer y escribir archivos
- ✅ Hacer requests HTTP (curl)
- ✅ Buscar patrones en código (grep)
- ✅ Editar archivos con find/replace
- ✅ Tomar decisiones basadas en los resultados

## 🚀 Quick Start

### 1. Iniciar LM Studio

```bash
# Asegúrate de que LM Studio esté ejecutándose en localhost:1234
# Y que tengas un modelo cargado (DeepSeek R1, GPT, etc.)
```

### 2. Ejecutar el Agente

```bash
# Modo interactivo
cd Plataforma/npm
node src/agent/agent-autonomous.js

# Modo single-shot
node src/agent/agent-autonomous.js "Crea un archivo hello.txt con Hello World"

# Con debug
DEBUG=true node src/agent/agent-autonomous.js
```

### 3. Probar con Tests Automatizados

```bash
node test-autonomous-agent.js
```

## 📚 Ejemplos Prácticos

### Ejemplo 1: Crear y editar archivos

```
💬 You: Crea un archivo config.json con { "port": 3000 } y luego cámbialo a puerto 8080

🤖 Agente:
[Iteración 1] Ejecuta WriteFile → Crea config.json
[Iteración 2] Ejecuta EditFile → Cambia puerto
✅ Completado: Archivo creado y modificado correctamente
```

### Ejemplo 2: Análisis de código

```
💬 You: Encuentra todos los TODOs en el directorio src y hazme un resumen

🤖 Agente:
[Iteración 1] Ejecuta Grep → Busca "TODO" en src/
[Iteración 2] Analiza resultados
✅ Encontré 15 TODOs distribuidos en 8 archivos:
   - src/agent/agent.js: 5 TODOs
   - src/formatter/markdown.js: 3 TODOs
   ...
```

### Ejemplo 3: Automatización con API

```
💬 You: Obtén el clima de Madrid desde wttr.in y guárdalo en clima.txt

🤖 Agente:
[Iteración 1] Ejecuta Curl → GET wttr.in/Madrid
[Iteración 2] Ejecuta WriteFile → Guarda respuesta
✅ Clima obtenido y guardado en clima.txt
```

## 🧠 ¿Cómo Funciona?

### Arquitectura

```
Usuario → Agente → LLM (con system prompt) → Tool Call Detection
                                                      ↓
                                              Ejecuta Herramienta
                                                      ↓
                                              Resultado al LLM
                                                      ↓
                                            ¿Más acciones? → Loop
                                                      ↓ No
                                            Respuesta Final
```

### System Prompt (El "Adoctrinamiento")

El agente funciona porque el LLM recibe un **system prompt** que le enseña:

1. **Qué herramientas tiene disponibles**
   ```
   - Bash: Ejecutar comandos
   - ReadFile: Leer archivos
   - WriteFile: Crear/sobrescribir
   - EditFile: Modificar con find/replace
   - Curl: HTTP requests
   - Grep: Buscar patrones
   ```

2. **Cómo invocar herramientas**
   ```json
   {
     "tool": "ToolName",
     "parameters": {...},
     "reasoning": "why"
   }
   ```

3. **Reglas de seguridad**
   - No ejecutar comandos destructivos sin confirmación
   - Validar paths
   - Manejar errores apropiadamente

4. **Workflow**
   - Analizar tarea → Planear → Ejecutar → Verificar → Reportar

## 🛠️ Herramientas Disponibles

| Herramienta | Descripción | Ejemplo |
|-------------|-------------|---------|
| **Bash** | Ejecuta comandos de sistema | `ls -la`, `npm install`, `git status` |
| **ReadFile** | Lee contenido de archivos | Leer `config.json` |
| **WriteFile** | Crea o sobrescribe archivos | Crear `output.txt` |
| **EditFile** | Edita con find/replace | Cambiar versión en `package.json` |
| **Curl** | HTTP requests GET/POST | API calls, descargar datos |
| **Grep** | Busca patrones en archivos | Encontrar TODOs, errores |

## 🔒 Seguridad

### Comandos Bloqueados

El agente **automáticamente bloquea** comandos peligrosos:

```bash
❌ rm -rf /
❌ format C:
❌ del /f /q *.*
❌ DROP TABLE users
```

### Validación

- ✅ Valida paths antes de leer/escribir
- ✅ Escapa argumentos de shell
- ✅ Límite de timeout por herramienta (30s default)
- ✅ Límite de iteraciones (10 default)

### Mejores Prácticas

1. **Revisar comandos antes de confirmar** (especialmente en producción)
2. **Ejecutar en directorio sandbox** cuando sea posible
3. **Usar variables de entorno** para configuración sensible
4. **Monitorear logs** para detectar comportamiento anormal

## 📖 Documentación Completa

- **[AGENT_SYSTEM_PROMPT.md](./AGENT_SYSTEM_PROMPT.md)**: System prompt completo y cómo funciona
- **[AUTONOMOUS_AGENT_GUIDE.md](./AUTONOMOUS_AGENT_GUIDE.md)**: Guía detallada con ejemplos avanzados
- **[agent-autonomous.js](../src/agent/agent-autonomous.js)**: Código fuente del agente

## 🎓 Casos de Uso

### Desarrollo

```
✅ "Encuentra todos los archivos que importan 'axios' y lista sus rutas"
✅ "Busca funciones sin JSDoc y hazme una lista"
✅ "Analiza package.json y dime qué dependencias están desactualizadas"
```

### DevOps

```
✅ "Verifica que nginx esté ejecutándose y muéstrame su estado"
✅ "Lee el log más reciente y resume los errores"
✅ "Crea un backup de la base de datos en /backups"
```

### Análisis de Datos

```
✅ "Lee sales.csv y calcula el total de ventas"
✅ "Descarga los datos de la API y guárdalos en formato JSON"
✅ "Procesa todos los .txt en /data y genera un resumen"
```

### Automatización

```
✅ "Cada vez que encuentres un TODO antiguo, crea un issue en GitHub"
✅ "Monitorea el uso de CPU y alértame si supera 80%"
✅ "Genera un reporte diario del estado del sistema"
```

## ⚙️ Configuración Avanzada

### Personalizar el System Prompt

Edita `SYSTEM_PROMPT` en `agent-autonomous.js` para:

- Cambiar la personalidad del agente
- Agregar reglas específicas de tu dominio
- Definir workflows personalizados
- Agregar contexto específico del proyecto

### Agregar Nuevas Herramientas

```javascript
// En agent-autonomous.js
TOOLS.MyCustomTool = async (params) => {
  // Tu implementación
  return {
    success: true,
    data: result
  };
};

// Actualiza SYSTEM_PROMPT para documentar la nueva herramienta
```

### Ajustar Comportamiento

```javascript
const agent = new AutonomousAgent({
  maxIterations: 20,      // Más iteraciones para tareas complejas
  debug: true,            // Ver cada paso del agente
  temperature: 0.0        // Más determinístico (menos creativo)
});
```

## 🐛 Troubleshooting

### "El agente no ejecuta herramientas"

**Problema**: El LLM no genera el formato JSON correcto

**Solución**:
```bash
# Usar temperatura más baja
DEBUG=true node src/agent/agent-autonomous.js
# Revisa si el LLM está generando JSON válido
```

### "Loop infinito"

**Problema**: El agente repite la misma acción sin progreso

**Solución**:
- Reduce `maxIterations` a 5-10
- Mejora el system prompt con ejemplos más claros
- Usa un modelo más capaz (Claude Opus, GPT-4, etc.)

### "Error de permisos"

**Problema**: No puede leer/escribir archivos

**Solución**:
```bash
# Verifica permisos del directorio
ls -la

# Ejecuta con permisos apropiados (cuidado en producción)
sudo node src/agent/agent-autonomous.js
```

## 🔮 Próximas Mejoras

Funcionalidades planeadas:

- [ ] Multi-tool execution (herramientas en paralelo)
- [ ] Memoria persistente entre sesiones
- [ ] Integración con MCP (Model Context Protocol)
- [ ] Sandbox seguro con Docker
- [ ] UI web para visualizar el flujo
- [ ] Streaming de respuestas token por token
- [ ] Soporte para más modelos (Claude API, OpenAI API)

## 📊 Comparación con Otros Agentes

| Característica | Este Agente | LangChain | AutoGPT |
|----------------|-------------|-----------|---------|
| **Instalación** | Simple (npm) | Media | Compleja |
| **Dependencias** | Pocas | Muchas | Muchas |
| **Local First** | ✅ Sí | ⚠️ Híbrido | ❌ No |
| **Tool Calling** | ✅ Nativo | ✅ Sí | ✅ Sí |
| **Customizable** | ✅ Muy | ⚠️ Medio | ⚠️ Medio |
| **Tamaño** | ~500 líneas | ~10K líneas | ~20K líneas |

## 💡 Tips y Trucos

### Tip 1: Especifica claramente la tarea

❌ Malo: "Haz algo con los archivos"
✅ Bueno: "Lee todos los .txt en /docs y cuenta cuántas palabras hay en total"

### Tip 2: Usa confirmación para operaciones destructivas

```
💬 You: Elimina todos los archivos .tmp en /temp, pero pregúntame antes

🤖 Agente: Encontré 15 archivos .tmp. ¿Confirmas que quieres eliminarlos? (y/n)
```

### Tip 3: Debugging con verbose

```bash
DEBUG=true node src/agent/agent-autonomous.js

# Verás:
# [USER]: mensaje
# [ASSISTANT]: respuesta
# [TOOL]: bash
# [RESULT]: {...}
```

### Tip 4: Combina herramientas

```
"Lee config.json, obtén la URL de API de ahí, haz un request,
 y guarda el resultado en output.json"
```

El agente automáticamente encadenará:
1. ReadFile (config.json)
2. Parse JSON
3. Curl (API)
4. WriteFile (output.json)

## 🤝 Contribuir

Para agregar nuevas herramientas o mejorar el agente:

1. Fork el repo
2. Agrega tu herramienta en `TOOLS`
3. Actualiza el `SYSTEM_PROMPT`
4. Crea tests en `test-autonomous-agent.js`
5. Pull request!

## 📄 Licencia

MIT

---

**¿Preguntas?** Lee la [documentación completa](./AUTONOMOUS_AGENT_GUIDE.md) o abre un issue.

**¿Problemas?** Revisa [troubleshooting](#-troubleshooting) o consulta los logs con `DEBUG=true`.

**¿Ideas?** Comparte tus casos de uso y herramientas personalizadas!

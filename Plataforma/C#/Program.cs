using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

var baseUrl = Environment.GetEnvironmentVariable("LMSTUDIO_URL") ?? "http://localhost:1234";
var modelId = "gpt-oss-20b-gpt-5-reasoning-distill";
var dbConnectionString = DbConfig.ResolveConnectionString(Environment.GetEnvironmentVariable("DB_CONNECTION_STRING"));

try
{
    var app = await AgentApp.BuildAsync(baseUrl, modelId, dbConnectionString);
    await app.RunConsoleAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Error crítico: {ex.Message}");
    Console.WriteLine("Presiona cualquier tecla para salir...");
    Console.ReadKey();
}

internal sealed class AgentApp
{
    private readonly Agent _agent;

    private AgentApp(Agent agent)
    {
        _agent = agent;
    }

    public static async Task<AgentApp> BuildAsync(string baseUrl, string? modelId, string? dbConnectionString, CancellationToken cancellationToken = default)
    {
        var llmClient = new LmStudioClient(baseUrl);
        var resolvedModel = string.IsNullOrWhiteSpace(modelId)
            ? await llmClient.ResolveDefaultModelAsync(cancellationToken) ?? throw new InvalidOperationException("No se encontró un modelo en LM Studio. Configure LMSTUDIO_MODEL o cargue un modelo.")
            : modelId!;

        var tools = ToolRegistry.Create(dbConnectionString);
        var agent = new Agent(llmClient, resolvedModel, tools, dbConnectionString);
        return new AgentApp(agent);
    }

    public async Task RunConsoleAsync(CancellationToken cancellationToken = default)
    {
        Ui.Banner(_agent.ModelId, _agent.BaseUrl);

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("🤖 > ");
            var input = Ui.ReadLineWithCancel();
            if (input is null)
            {
                Ui.Warn("Entrada cancelada (ESC).");
                continue;
            }

            if (input.Trim().Equals("/exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (input.Trim().Equals("/clear", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                Ui.Banner(_agent.ModelId, _agent.BaseUrl);
                continue;
            }

            if (input.Trim().StartsWith("/load", StringComparison.OrdinalIgnoreCase))
            {
                await HandleLoadCommandAsync(input);
                continue;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            try
            {
                if (Ui.IsShellCommand(input))
                {
                    Ui.Info("Ejecutando comando local...");
                    var shellResult = await Ui.ExecuteLocalShellAsync(input, cancellationToken);
                    Ui.Success($"Salida (exit {shellResult.exitCode}):");
                    Console.WriteLine(shellResult.stdout.Trim());
                    if (!string.IsNullOrWhiteSpace(shellResult.stderr))
                    {
                        Ui.Warn(shellResult.stderr.Trim());
                    }
                    continue;
                }

                Ui.Info("🧠 Pensando...");
                var result = await _agent.ExecuteWithReasoningAsync(input,
                    thought => Ui.Thinking(thought),
                    action => Ui.Action(action),
                    cancellationToken);

                Ui.Success("✅ Respuesta:");
                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Ui.Error($"Error: {ex.Message}");
            }
        }
    }

    private async Task HandleLoadCommandAsync(string input)
    {
        var parts = input.Split(' ', 2);
        if (parts.Length > 1)
        {
            var filePath = parts[1].Trim('"', '\'');
            if (File.Exists(filePath))
            {
                var content = await File.ReadAllTextAsync(filePath);
                Ui.Info($"📁 Cargado archivo: {filePath}");
                Console.WriteLine(content);
            }
            else
            {
                Ui.Error($"Archivo no encontrado: {filePath}");
            }
        }
    }
}

internal sealed class Agent
{
    private readonly LmStudioClient _llm;
    private readonly Dictionary<string, ITool> _tools;
    private readonly AgentContext _context;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Agent(LmStudioClient llm, string modelId, IEnumerable<ITool> tools, string? dbConnectionString)
    {
        _llm = llm;
        ModelId = modelId;
        BaseUrl = llm.BaseUrl;
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _context = new AgentContext
        {
            DatabaseConnectionString = dbConnectionString,
            CurrentDirectory = Directory.GetCurrentDirectory()
        };
    }

    public string ModelId { get; }
    public string BaseUrl { get; }

    public async Task<string> ExecuteWithReasoningAsync(string userInput,
        Action<string>? onThought = null,
        Action<string>? onAction = null,
        CancellationToken cancellationToken = default)
    {
        // Detectar si es conversacional primero
        if (IsConversationalInput(userInput))
        {
            return GenerateConversationalResponse(userInput);
        }

        var reasoning = await GenerateReasoningStepAsync(userInput, cancellationToken);
        onThought?.Invoke(reasoning.Thought);

        // Ejecutar acción si existe
        if (!string.IsNullOrEmpty(reasoning.ToolName))
        {
            onAction?.Invoke($"Ejecutando: {reasoning.Action}");
            var result = await ExecuteActionAsync(reasoning.ToolName, reasoning.ToolArguments, cancellationToken);
            onThought?.Invoke($"Observación: {result}");

            return FormatToolResult(result);
        }

        // Manejar casos especiales sin herramienta
        if (reasoning.Action?.Contains("directorio actual") == true)
        {
            return $"📁 Directorio actual: {_context.CurrentDirectory}";
        }

        return reasoning.Thought;
    }

    private async Task<ReasoningStep> GenerateReasoningStepAsync(string userInput, CancellationToken cancellationToken)
    {
        var prompt = BuildReasoningPrompt(userInput);

        // ✅ USAR CHAT COMPLETIONS API (compatible con tu modelo)
        var request = new ChatCompletionRequest
        {
            Model = ModelId,
            Messages = new List<ChatMessage>
            {
                new("system", "Eres un asistente CLI inteligente. Responde SOLO con JSON válido."),
                new("user", prompt)
            },
            Temperature = 0.1,
            MaxTokens = 500
        };

        try
        {
            var response = await _llm.CreateChatCompletionAsync(request, cancellationToken);
            var content = response.Choices.First().Message.Content ?? "{}";
            return ParseReasoningResponse(content);
        }
        catch (Exception ex)
        {
            // Log del error para debugging
            Console.WriteLine($"[DEBUG] Error en LLM: {ex.Message}");
            // Fallback inteligente basado en el input
            return CreateIntelligentFallback(userInput);
        }
    }

    private string BuildReasoningPrompt(string userInput)
    {
        var toolsList = string.Join(", ", _tools.Keys);

        return $$$"""
    El usuario dijo: "{{{userInput}}}"

    Herramientas disponibles: {{{toolsList}}}
    Directorio actual: {{{_context.CurrentDirectory}}}

    Analiza la solicitud y responde SOLO con JSON válido:

    {
        "thought": "breve análisis del pedido del usuario",
        "action": "descripción de qué hacer",
        "tool_name": "nombre_herramienta o null si es conversación",
        "tool_arguments": {}
    }

    Ejemplos:
    - Para "lista archivos": {"thought": "Usuario quiere ver archivos", "action": "Listar directorio", "tool_name": "list_directory", "tool_arguments": {"path": "."}}
    - Para "hola": {"thought": "Saludo amistoso", "action": "Responder saludo", "tool_name": null}
    - Para "busca archivos txt": {"thought": "Buscar archivos de texto", "action": "Buscar archivos", "tool_name": "search_files", "tool_arguments": {"pattern": "*.txt"}}
    """;
    }

    private ReasoningStep CreateIntelligentFallback(string input)
    {
        var inputLower = input.ToLowerInvariant();

        // Detectar preguntas generales que requieren web search
        if ((inputLower.Contains("quien") || inputLower.Contains("qué es") || inputLower.Contains("que es") ||
             inputLower.Contains("cuál es") || inputLower.Contains("cómo") || inputLower.Contains("cuando") ||
             inputLower.Contains("dónde") || inputLower.Contains("por qué")) &&
            !inputLower.Contains("archivo") && !inputLower.Contains("directorio") && !inputLower.Contains("carpeta"))
        {
            return new ReasoningStep
            {
                Thought = "Pregunta general que requiere búsqueda web",
                Action = "Buscando información en la web",
                ToolName = "web_search",
                ToolArguments = JsonDocument.Parse($$"""{"query": "{{input}}"}""").RootElement
            };
        }

        // Detectar comando para mostrar directorio actual
        if (inputLower.Contains("en que directorio") || inputLower.Contains("directorio actual") ||
            inputLower.Contains("pwd") || (inputLower.Contains("donde") && inputLower.Contains("estoy")))
        {
            return new ReasoningStep
            {
                Thought = "Usuario quiere saber el directorio actual",
                Action = "Mostrando directorio actual",
                ToolName = null,  // No requiere herramienta, se maneja en el contexto
                ToolArguments = null
            };
        }

        // Detectar listado de directorio específico
        if (inputLower.Contains("qué hay en") || inputLower.Contains("que hay en") ||
            inputLower.Contains("listar") || inputLower.Contains("muestra"))
        {
            // Intentar extraer el nombre del directorio
            var directory = ExtractDirectoryPath(input, inputLower);
            return new ReasoningStep
            {
                Thought = $"Usuario quiere ver contenido de {directory}",
                Action = $"Listando contenido de {directory}",
                ToolName = "list_directory",
                ToolArguments = JsonDocument.Parse($$"""{"path": "{{directory}}"}""").RootElement
            };
        }

        // Detectar listado simple de archivos
        if (inputLower.Contains("archivos") || inputLower.Contains("carpeta") ||
            inputLower.Contains("contenido") || inputLower.Contains("ls"))
        {
            return new ReasoningStep
            {
                Thought = "Usuario quiere ver archivos del directorio actual",
                Action = "Listando contenido del directorio",
                ToolName = "list_directory",
                ToolArguments = JsonDocument.Parse("""{"path": "."}""").RootElement
            };
        }
        else if ((inputLower.Contains("leer") || inputLower.Contains("ver")) && inputLower.Contains("archivo"))
        {
            return new ReasoningStep
            {
                Thought = "Usuario quiere leer un archivo, necesito buscar archivos disponibles primero",
                Action = "Buscando archivos para leer",
                ToolName = "list_directory",
                ToolArguments = JsonDocument.Parse("""{"path": "."}""").RootElement
            };
        }
        else if (inputLower.Contains("buscar") || inputLower.Contains("encontrar") || inputLower.Contains("search"))
        {
            // Extraer término de búsqueda simple
            var searchTerm = ExtractSearchTerm(input);
            return new ReasoningStep
            {
                Thought = $"Usuario quiere buscar: {searchTerm}",
                Action = "Realizando búsqueda",
                ToolName = "search_files",
                ToolArguments = JsonDocument.Parse($$"""{"pattern": "*{{searchTerm}}*", "path": "."}""").RootElement
            };
        }
        else if (IsConversationalInput(input))
        {
            return new ReasoningStep
            {
                Thought = "Es una conversación, no requiere acción de herramienta",
                Action = "Respondiendo conversación",
                ToolName = null,
                ToolArguments = null
            };
        }
        else
        {
            // Por defecto, pregunta conversacional genérica
            return new ReasoningStep
            {
                Thought = "Pregunta general, buscaré información en la web",
                Action = "Buscando respuesta en la web",
                ToolName = "web_search",
                ToolArguments = JsonDocument.Parse($$"""{"query": "{{input}}"}""").RootElement
            };
        }
    }

    private string ExtractDirectoryPath(string input, string inputLower)
    {
        // Buscar patrones como "que hay en documentos", "lista /tmp", etc.
        if (inputLower.Contains("en "))
        {
            var parts = input.Split(new[] { " en ", " EN " }, StringSplitOptions.None);
            if (parts.Length > 1)
            {
                var path = parts[1].Trim();
                // Remover palabras comunes al final
                path = path.Replace("?", "").Trim();
                return string.IsNullOrWhiteSpace(path) ? "." : path;
            }
        }

        // Buscar una palabra que parezca una ruta
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var cleanWord = word.Trim('?', '.', ',', '!');
            if (cleanWord.Contains("/") || cleanWord.Contains("\\") || cleanWord.Contains(":"))
                return cleanWord;
        }

        return ".";
    }

    private string ExtractSearchTerm(string input)
    {
        var words = input.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Contains("buscar") || words[i].Contains("encontrar") || words[i].Contains("search"))
            {
                return i + 1 < words.Length ? words[i + 1] : "archivo";
            }
        }
        return "archivo";
    }

    private bool IsConversationalInput(string input)
    {
        var lower = input.ToLowerInvariant();
        return lower.Contains("hola") || lower.Contains("adiós") || lower.Contains("gracias") ||
               lower.Contains("cómo estás") || lower.Contains("qué tal") || lower.Contains("buenos días") ||
               lower.Contains("buenas tardes") || lower.Contains("buenas noches") ||
               lower.Contains("hi") || lower.Contains("hello") || lower.Contains("bye") ||
               lower.Contains("quién eres") || lower.Contains("qué puedes hacer");
    }

    private string GenerateConversationalResponse(string input)
    {
        var lower = input.ToLowerInvariant();

        if (lower.Contains("hola") || lower.Contains("hi") || lower.Contains("hello"))
            return "¡Hola! Soy tu asistente CLI inteligente. ¿En qué puedo ayudarte?";
        if (lower.Contains("cómo estás") || lower.Contains("qué tal"))
            return "¡Estoy funcionando perfectamente! Listo para ayudarte con tareas de sistema, archivos, base de datos y más.";
        if (lower.Contains("gracias"))
            return "¡De nada! Estoy aquí para ayudarte. ¿Necesitas algo más?";
        if (lower.Contains("adiós") || lower.Contains("chao") || lower.Contains("bye"))
            return "¡Hasta luego! Vuelve cuando necesites ayuda.";
        if (lower.Contains("quién eres") || lower.Contains("qué eres"))
            return "Soy un agente CLI inteligente que puede ayudarte con archivos, comandos shell, base de datos, búsquedas web y más.";
        if (lower.Contains("qué puedes hacer"))
            return "Puedo: listar archivos, leer/escribir archivos, ejecutar comandos, consultar bases de datos, buscar en la web, gestionar procesos y analizar código.";

        return "¡Hola! ¿En qué puedo asistirte hoy?";
    }

    private async Task<string> ExecuteActionAsync(string toolName, JsonElement? toolArgs, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            return $"Herramienta '{toolName}' no disponible";
        }

        try
        {
            var result = await tool.ExecuteAsync(toolArgs ?? JsonDocument.Parse("{}").RootElement, cancellationToken);
            UpdateContextFromAction(toolName, toolArgs, result);
            return result;
        }
        catch (Exception ex)
        {
            return $"Error ejecutando {toolName}: {ex.Message}";
        }
    }

    private void UpdateContextFromAction(string toolName, JsonElement? toolArgs, string result)
    {
        try
        {
            switch (toolName)
            {
                case "list_directory":
                    if (toolArgs?.TryGetProperty("path", out var pathProp) == true)
                    {
                        var path = pathProp.GetString();
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        {
                            _context.CurrentDirectory = Path.GetFullPath(path);
                        }
                    }
                    break;
            }
        }
        catch
        {
            // Ignorar errores de contexto
        }
    }

    private string FormatToolResult(string result)
    {
        try
        {
            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    // Formatear lista de archivos
                    if (data.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        var fileList = new List<string>();
                        foreach (var item in items.EnumerateArray())
                        {
                            if (item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                            {
                                var type = item.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "file";
                                var size = item.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0;
                                var sizeStr = type == "file" ? $" ({FormatFileSize(size)})" : "";
                                fileList.Add($"{type[0]}: {name.GetString()}{sizeStr}");
                            }
                        }
                        
                        if (fileList.Count > 0)
                        {
                            var path = data.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : ".";
                            return $"📁 Contenido de {path}:\n" + string.Join("\n", fileList.Take(20)) +
                                   (fileList.Count > 20 ? $"\n... y {fileList.Count - 20} más" : "");
                        }
                    }

                    // Formatear resultados de búsqueda de archivos
                    if (data.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                    {
                        var resultList = new List<string>();
                        foreach (var item in results.EnumerateArray())
                        {
                            if (item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                            {
                                resultList.Add($"📄 {name.GetString()}");
                            }
                            else if (item.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
                            {
                                resultList.Add($"📄 {Path.GetFileName(path.GetString())}");
                            }
                        }

                        if (resultList.Count > 0)
                        {
                            return $"🔍 Encontré {resultList.Count} resultados:\n" + string.Join("\n", resultList.Take(10)) +
                                   (resultList.Count > 10 ? $"\n... y {resultList.Count - 10} más" : "");
                        }
                    }

                    // Formatear resultados de búsqueda web
                    if (data.TryGetProperty("query", out var query))
                    {
                        var output = new List<string>();

                        if (data.TryGetProperty("heading", out var heading) && !string.IsNullOrWhiteSpace(heading.GetString()))
                        {
                            output.Add($"📌 {heading.GetString()}");
                            output.Add("");
                        }

                        if (data.TryGetProperty("abstract", out var abstractProp) && !string.IsNullOrWhiteSpace(abstractProp.GetString()))
                        {
                            output.Add($"ℹ️  {abstractProp.GetString()}");

                            if (data.TryGetProperty("url", out var url) && !string.IsNullOrWhiteSpace(url.GetString()))
                            {
                                output.Add($"🔗 Más información: {url.GetString()}");
                            }
                            output.Add("");
                        }

                        if (data.TryGetProperty("related_topics", out var topics) && topics.ValueKind == JsonValueKind.Array)
                        {
                            output.Add("📚 Temas relacionados:");
                            foreach (var topic in topics.EnumerateArray())
                            {
                                if (topic.TryGetProperty("text", out var text))
                                {
                                    output.Add($"  • {text.GetString()}");
                                }
                            }
                        }

                        if (data.TryGetProperty("message", out var message))
                        {
                            output.Add($"⚠️  {message.GetString()}");
                        }

                        if (output.Count > 0)
                            return string.Join("\n", output);
                    }

                    return "✅ Operación completada exitosamente";
                }
            }

            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                return $"❌ Error: {error.GetString()}";
            }
        }
        catch
        {
            // Si no se puede parsear, devolver el resultado original
        }

        return result.Length > 300 ? result.Substring(0, 300) + "..." : result;
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private ReasoningStep ParseReasoningResponse(string response)
    {
        try
        {
            // Limpiar respuesta - buscar JSON
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                JsonElement toolArgs;
                if (root.TryGetProperty("tool_arguments", out var args) && args.ValueKind == JsonValueKind.Object)
                {
                    toolArgs = args.Clone();
                }
                else
                {
                    toolArgs = JsonDocument.Parse("{}").RootElement;
                }

                return new ReasoningStep
                {
                    Thought = root.TryGetProperty("thought", out var thought) ? thought.GetString() ?? "Analizando..." : "Analizando...",
                    Action = root.TryGetProperty("action", out var action) ? action.GetString() ?? "" : "",
                    ToolName = root.TryGetProperty("tool_name", out var tool) ? tool.GetString() : null,
                    ToolArguments = toolArgs
                };
            }
        }
        catch (JsonException)
        {
            // Si no es JSON válido, buscar patrones simples
            if (response.ToLower().Contains("listar") || response.ToLower().Contains("archivos") || response.ToLower().Contains("directory"))
            {
                return new ReasoningStep
                {
                    Thought = "Listando directorio: " + response,
                    Action = "Explorar archivos",
                    ToolName = "list_directory",
                    ToolArguments = JsonDocument.Parse("""{"path": "."}""").RootElement
                };
            }
        }

        // Fallback a exploración
        return new ReasoningStep
        {
            Thought = "No pude entender claramente, voy a explorar el entorno",
            Action = "Listando directorio actual para entender el contexto",
            ToolName = "list_directory",
            ToolArguments = JsonDocument.Parse("""{"path": "."}""").RootElement
        };
    }
}

internal record ReasoningStep
{
    public string Thought { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public JsonElement? ToolArguments { get; set; }
}

internal sealed class AgentContext
{
    public string CurrentDirectory { get; set; } = Directory.GetCurrentDirectory();
    public string? CurrentDatabase { get; set; }
    public string? DatabaseConnectionString { get; set; }
}

internal sealed class LmStudioClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public LmStudioClient(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(120)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        BaseUrl = baseUrl.TrimEnd('/');
    }

    public string BaseUrl { get; }

    public async Task<string?> ResolveDefaultModelAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetAsync("/v1/models", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;

            var models = data.EnumerateArray()
                .Select(m => m.GetProperty("id").GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            return models.FirstOrDefault(m => !m.Contains("embedding"));
        }
        catch
        {
            return null;
        }
    }

    // ✅ CHAT COMPLETIONS API (compatible con tu modelo)
    public async Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(request, _jsonOptions);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await _http.SendAsync(httpRequest, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"LLM error: {(int)response.StatusCode} - {raw}");

        var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(raw, _jsonOptions);
        return completion ?? throw new InvalidOperationException("Respuesta del LLM vacía o inválida.");
    }
}

internal record ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required List<ChatMessage> Messages { get; init; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 0.1;

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }
}

internal record ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public required List<ChatChoice> Choices { get; init; }
}

internal record ChatChoice
{
    [JsonPropertyName("message")]
    public required ChatMessage Message { get; init; }
}

internal record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal static class Ui
{
    public static void Banner(string modelId, string baseUrl)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("🚀 Agente CLI Avanzado");
        Console.ResetColor();
        Console.WriteLine($"Modelo: {modelId}");
        Console.WriteLine($"Endpoint: {baseUrl}");
        Console.WriteLine("Comandos: !comando, /exit, /clear, /load <archivo>");
        Console.WriteLine();
    }

    public static void Thinking(string thought)
    {
        if (!string.IsNullOrWhiteSpace(thought))
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"   💭 {thought}");
            Console.ResetColor();
        }
    }

    public static void Action(string action)
    {
        if (!string.IsNullOrWhiteSpace(action))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"   🛠️  {action}");
            Console.ResetColor();
        }
    }

    public static bool IsShellCommand(string input)
    {
        var trimmed = input.TrimStart();
        return trimmed.StartsWith("!", StringComparison.Ordinal) ||
               trimmed.StartsWith("cmd ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("powershell ", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<(int exitCode, string stdout, string stderr)> ExecuteLocalShellAsync(string input, CancellationToken cancellationToken)
    {
        var command = input.Trim();
        if (command.StartsWith("!")) command = command[1..].TrimStart();

        var fileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash";
        var args = OperatingSystem.IsWindows()
            ? $"-NoProfile -Command \"{command}\""
            : $"-c \"{command}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null) return (-1, string.Empty, "No se pudo iniciar el proceso.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, stdout, stderr);
    }

    public static void Info(string message) => WriteColored($"ℹ️  {message}", ConsoleColor.Blue);
    public static void Success(string message) => WriteColored($"✅ {message}", ConsoleColor.Green);
    public static void Warn(string message) => WriteColored($"⚠️  {message}", ConsoleColor.Yellow);
    public static void Error(string message) => WriteColored($"❌ {message}", ConsoleColor.Red);

    public static string? ReadLineWithCancel()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }
            if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                return null;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    Console.Write("\b \b");
                }
                continue;
            }
            buffer.Append(key.KeyChar);
            Console.Write(key.KeyChar);
        }
    }

    private static void WriteColored(string message, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        Console.ForegroundColor = previous;
    }
}

internal static class ToolRegistry
{
    public static IReadOnlyList<ITool> Create(string? dbConnectionString)
    {
        var httpClient = new HttpClient();
        return new List<ITool>
        {
            new ReadFileTool(),
            new WriteFileTool(),
            new ListDirectoryTool(),
            new SearchFilesTool(),
            new RunShellTool(),
            new SqlCommandTool(dbConnectionString),
            new ListDatabasesTool(dbConnectionString),
            new ListTablesTool(dbConnectionString),
            new HttpRequestTool(httpClient),
            new WebSearchTool(httpClient),
            new ListProcessesTool(),
            new KillProcessTool(),
            new AnalyzeCodeTool(),
            new SearchCodeTool()
        };
    }
}

internal interface ITool
{
    string Name { get; }
    ToolDefinition Definition { get; }
    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default);
}

internal record ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public required ToolFunction Function { get; init; }
}

internal record ToolFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("parameters")]
    public required object Parameters { get; init; }
}

internal static class ToolHelpers
{
    public static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            }
            : null;
    }

    public static bool GetBool(JsonElement element, string propertyName, bool defaultValue = false)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
                _ => defaultValue
            };
        }
        return defaultValue;
    }

    public static int? GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var num)) return num;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return parsed;
        }
        return null;
    }
}

internal static class ToolResult
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Success(object payload) =>
        JsonSerializer.Serialize(new { ok = true, data = payload }, Options);

    public static string Error(string message) =>
        JsonSerializer.Serialize(new { ok = false, error = message }, Options);
}

internal sealed class ReadFileTool : ITool
{
    public string Name => "read_file";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "read_file",
            Description = "Lee el contenido de un archivo de texto",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Ruta del archivo" }
                },
                required = new[] { "path" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolHelpers.GetString(arguments, "path");
        if (string.IsNullOrWhiteSpace(path))
            return ToolResult.Error("Falta el parámetro 'path'");

        if (!File.Exists(path))
            return ToolResult.Error($"Archivo no encontrado: {path}");

        try
        {
            var content = await File.ReadAllTextAsync(path, cancellationToken);
            return ToolResult.Success(new { path, content, size = content.Length });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error leyendo archivo: {ex.Message}");
        }
    }
}

internal sealed class WriteFileTool : ITool
{
    public string Name => "write_file";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "write_file",
            Description = "Escribe contenido en un archivo",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Ruta del archivo" },
                    content = new { type = "string", description = "Contenido a escribir" }
                },
                required = new[] { "path", "content" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolHelpers.GetString(arguments, "path");
        var content = ToolHelpers.GetString(arguments, "content");

        if (string.IsNullOrWhiteSpace(path))
            return ToolResult.Error("Falta el parámetro 'path'");

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(path, content ?? "", cancellationToken);
            return ToolResult.Success(new { path, action = "write" });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error escribiendo archivo: {ex.Message}");
        }
    }
}

internal sealed class ListDirectoryTool : ITool
{
    public string Name => "list_directory";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "list_directory",
            Description = "Lista archivos y directorios",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Ruta del directorio" }
                }
            }
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolHelpers.GetString(arguments, "path") ?? ".";

        if (!Directory.Exists(path))
            return Task.FromResult(ToolResult.Error($"Directorio no encontrado: {path}"));

        try
        {
            var files = Directory.EnumerateFiles(path)
                .Select(f => new {
                    name = Path.GetFileName(f),
                    path = f,
                    type = "file",
                    size = new FileInfo(f).Length
                }).ToList();

            var directories = Directory.EnumerateDirectories(path)
                .Select(d => new {
                    name = Path.GetFileName(d),
                    path = d,
                    type = "directory",
                    size = 0L
                }).ToList();

            var items = files.Concat(directories).ToList();

            return Task.FromResult(ToolResult.Success(new { path, items }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error listando directorio: {ex.Message}"));
        }
    }
}

internal sealed class SearchFilesTool : ITool
{
    public string Name => "search_files";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "search_files",
            Description = "Busca archivos por nombre",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Ruta de búsqueda" },
                    pattern = new { type = "string", description = "Patrón de nombre" }
                }
            }
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolHelpers.GetString(arguments, "path") ?? ".";
        var pattern = ToolHelpers.GetString(arguments, "pattern") ?? "*";

        if (!Directory.Exists(path))
            return Task.FromResult(ToolResult.Error($"Directorio no encontrado: {path}"));

        try
        {
            var files = Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories)
                .Select(f => new
                {
                    path = f,
                    name = Path.GetFileName(f),
                    size = new FileInfo(f).Length
                }).ToList();

            return Task.FromResult(ToolResult.Success(new { path, pattern, results = files }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error buscando archivos: {ex.Message}"));
        }
    }
}

internal sealed class RunShellTool : ITool
{
    public string Name => "run_shell_command";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "run_shell_command",
            Description = "Ejecuta comandos de shell",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    command = new { type = "string", description = "Comando a ejecutar" }
                },
                required = new[] { "command" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var command = ToolHelpers.GetString(arguments, "command");

        if (string.IsNullOrWhiteSpace(command))
            return ToolResult.Error("Falta el parámetro 'command'");

        try
        {
            var fileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash";
            var args = OperatingSystem.IsWindows() ? $"-Command \"{command}\"" : $"-c \"{command}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
                return ToolResult.Error("No se pudo iniciar el proceso");

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);

            return ToolResult.Success(new
            {
                exitCode = process.ExitCode,
                stdout,
                stderr
            });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error ejecutando comando: {ex.Message}");
        }
    }
}

internal sealed class SqlCommandTool : ITool
{
    private readonly string? _connectionString;
    public SqlCommandTool(string? connectionString) => _connectionString = connectionString;
    public string Name => "sql_command";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "sql_command",
            Description = "Ejecuta SQL en base de datos",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    sql = new { type = "string", description = "Comando SQL" }
                },
                required = new[] { "sql" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return ToolResult.Error("Cadena de conexión no configurada");

        var sql = ToolHelpers.GetString(arguments, "sql");
        if (string.IsNullOrWhiteSpace(sql))
            return ToolResult.Error("Falta el parámetro 'sql'");

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            await using var command = new SqlCommand(sql, connection);
            
            if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var results = new List<Dictionary<string, object>>();

                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    results.Add(row);
                }
                return ToolResult.Success(new { rows = results, count = results.Count });
            }
            else
            {
                var affected = await command.ExecuteNonQueryAsync(cancellationToken);
                return ToolResult.Success(new { affectedRows = affected });
            }
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error ejecutando SQL: {ex.Message}");
        }
    }
}

internal sealed class ListDatabasesTool : ITool
{
    private readonly string? _connectionString;
    public ListDatabasesTool(string? connectionString) => _connectionString = connectionString;
    public string Name => "list_databases";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "list_databases",
            Description = "Lista bases de datos",
            Parameters = new { type = "object", properties = new { } }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return ToolResult.Error("Cadena de conexión no configurada");

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT name FROM sys.databases WHERE state = 0 ORDER BY name", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var databases = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                databases.Add(reader.GetString(0));
            }
            return ToolResult.Success(new { databases });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error listando bases de datos: {ex.Message}");
        }
    }
}

internal sealed class ListTablesTool : ITool
{
    private readonly string? _connectionString;
    public ListTablesTool(string? connectionString) => _connectionString = connectionString;
    public string Name => "list_tables";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "list_tables",
            Description = "Lista tablas en base de datos",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    database = new { type = "string", description = "Nombre de base de datos" }
                }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return ToolResult.Error("Cadena de conexión no configurada");

        var database = ToolHelpers.GetString(arguments, "database");
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(database))
            {
                await using var useDb = new SqlCommand($"USE [{database}]", connection);
                await useDb.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new SqlCommand(@"
                SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE 
                FROM INFORMATION_SCHEMA.TABLES 
                ORDER BY TABLE_SCHEMA, TABLE_NAME", connection);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var tables = new List<object>();

            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(new
                {
                    schema = reader.GetString(0),
                    name = reader.GetString(1),
                    type = reader.GetString(2)
                });
            }
            return ToolResult.Success(new { database = database ?? "(actual)", tables });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error listando tablas: {ex.Message}");
        }
    }
}

internal sealed class HttpRequestTool : ITool
{
    private readonly HttpClient _http;
    public HttpRequestTool(HttpClient http) => _http = http;
    public string Name => "http_request";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "http_request",
            Description = "Realiza peticiones HTTP",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    url = new { type = "string", description = "URL destino" }
                },
                required = new[] { "url" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var url = ToolHelpers.GetString(arguments, "url");
        if (string.IsNullOrWhiteSpace(url))
            return ToolResult.Error("Falta el parámetro 'url'");

        try
        {
            var response = await _http.GetAsync(url, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ToolResult.Success(new
            {
                status = (int)response.StatusCode,
                content
            });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error en petición HTTP: {ex.Message}");
        }
    }
}

internal sealed class WebSearchTool : ITool
{
    private readonly HttpClient _http;
    public WebSearchTool(HttpClient http) => _http = http;
    public string Name => "web_search";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "web_search",
            Description = "Busca en la web",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Término de búsqueda" }
                },
                required = new[] { "query" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var query = ToolHelpers.GetString(arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
            return ToolResult.Error("Falta el parámetro 'query'");

        try
        {
            var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1";
            var responseText = await _http.GetStringAsync(url);

            // Parsear respuesta de DuckDuckGo
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            var result = new Dictionary<string, object>
            {
                ["query"] = query
            };

            // Extraer información útil
            if (root.TryGetProperty("AbstractText", out var abstractText) && !string.IsNullOrWhiteSpace(abstractText.GetString()))
            {
                result["abstract"] = abstractText.GetString()!;
                if (root.TryGetProperty("AbstractURL", out var abstractUrl))
                    result["url"] = abstractUrl.GetString() ?? "";
            }
            else if (root.TryGetProperty("Abstract", out var abstractProp) && !string.IsNullOrWhiteSpace(abstractProp.GetString()))
            {
                result["abstract"] = abstractProp.GetString()!;
            }

            if (root.TryGetProperty("Heading", out var heading) && !string.IsNullOrWhiteSpace(heading.GetString()))
            {
                result["heading"] = heading.GetString()!;
            }

            // Extraer tópicos relacionados
            if (root.TryGetProperty("RelatedTopics", out var relatedTopics) && relatedTopics.ValueKind == JsonValueKind.Array)
            {
                var topics = new List<Dictionary<string, string>>();
                foreach (var topic in relatedTopics.EnumerateArray().Take(5))
                {
                    if (topic.TryGetProperty("Text", out var text) && !string.IsNullOrWhiteSpace(text.GetString()))
                    {
                        var topicDict = new Dictionary<string, string> { ["text"] = text.GetString()! };
                        if (topic.TryGetProperty("FirstURL", out var firstUrl))
                            topicDict["url"] = firstUrl.GetString() ?? "";
                        topics.Add(topicDict);
                    }
                }
                if (topics.Count > 0)
                    result["related_topics"] = topics;
            }

            // Si no hay información, indicarlo
            if (!result.ContainsKey("abstract") && !result.ContainsKey("related_topics"))
            {
                result["message"] = "No se encontró información en DuckDuckGo. Intenta reformular la pregunta o ser más específico.";
            }

            return ToolResult.Success(result);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error en búsqueda web: {ex.Message}");
        }
    }
}

internal sealed class ListProcessesTool : ITool
{
    public string Name => "list_processes";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "list_processes",
            Description = "Lista procesos en ejecución",
            Parameters = new { type = "object", properties = new { } }
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var processes = Process.GetProcesses()
                .OrderByDescending(p => p.WorkingSet64)
                .Take(20)
                .Select(p => new
                {
                    id = p.Id,
                    name = p.ProcessName,
                    memory = p.WorkingSet64 / 1024 / 1024
                })
                .ToList();

            return Task.FromResult(ToolResult.Success(new { processes }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error listando procesos: {ex.Message}"));
        }
    }
}

internal sealed class KillProcessTool : ITool
{
    public string Name => "kill_process";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "kill_process",
            Description = "Termina un proceso por ID",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    pid = new { type = "number", description = "ID del proceso" }
                },
                required = new[] { "pid" }
            }
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var pid = ToolHelpers.GetInt(arguments, "pid");

        if (pid is null)
            return Task.FromResult(ToolResult.Error("Falta el parámetro 'pid'"));

        try
        {
            var process = Process.GetProcessById(pid.Value);
            process.Kill();
            return Task.FromResult(ToolResult.Success(new { pid = pid.Value, status = "terminado" }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error terminando proceso: {ex.Message}"));
        }
    }
}

internal sealed class AnalyzeCodeTool : ITool
{
    public string Name => "analyze_code";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "analyze_code",
            Description = "Analiza código en un directorio",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Ruta del directorio o archivo" }
                }
            }
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolHelpers.GetString(arguments, "path") ?? ".";

        try
        {
            var codeFiles = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(f => IsCodeFile(f))
                .Take(50)
                .ToList();

            var analysis = new
            {
                totalFiles = codeFiles.Count,
                fileTypes = codeFiles.GroupBy(Path.GetExtension)
                    .ToDictionary(g => g.Key ?? "sin extensión", g => g.Count()),
                largestFiles = codeFiles.Select(f => new
                {
                    file = f,
                    size = new FileInfo(f).Length
                })
                .OrderByDescending(f => f.size)
                .Take(5)
                .ToList()
            };

            return Task.FromResult(ToolResult.Success(new { path, analysis }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error analizando código: {ex.Message}"));
        }
    }

    private static bool IsCodeFile(string filePath)
    {
        var extensions = new[] { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".html", ".css", ".xml", ".json", ".sql" };
        return extensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());
    }
}

internal sealed class SearchCodeTool : ITool
{
    public string Name => "search_code";
    public ToolDefinition Definition => new()
    {
        Type = "function",
        Function = new ToolFunction
        {
            Name = "search_code",
            Description = "Busca texto en archivos de código",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Ruta de búsqueda" },
                    pattern = new { type = "string", description = "Texto a buscar" }
                },
                required = new[] { "pattern" }
            }
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolHelpers.GetString(arguments, "path") ?? ".";
        var pattern = ToolHelpers.GetString(arguments, "pattern");

        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult(ToolResult.Error("Falta el parámetro 'pattern'"));

        try
        {
            var codeFiles = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(f => IsCodeFile(f))
                .Take(100)
                .ToList();

            var results = new List<object>();

            foreach (var file in codeFiles)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new
                        {
                            file,
                            matches = 1
                        });
                    }
                }
                catch
                {
                    // Ignorar archivos que no se pueden leer
                }
            }

            return Task.FromResult(ToolResult.Success(new { pattern, results }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error buscando en código: {ex.Message}"));
        }
    }

    private static bool IsCodeFile(string filePath)
    {
        var extensions = new[] { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".html", ".css", ".xml", ".json", ".sql" };
        return extensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());
    }
}

internal static class DbConfig
{
    public static string? ResolveConnectionString(string? envConn)
    {
        if (!string.IsNullOrWhiteSpace(envConn)) return envConn;

        // Buscar en archivos de configuración comunes
        var candidates = new[]
        {
            "appsettings.json",
            "config.json",
            Path.Combine("IPC", "DB", "appsettings.json")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                try
                {
                    var json = File.ReadAllText(candidate);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ConnectionString", out var cs) && cs.ValueKind == JsonValueKind.String)
                        return cs.GetString();
                }
                catch { /* Continuar con el siguiente */ }
            }
        }

        return envConn;
    }
}
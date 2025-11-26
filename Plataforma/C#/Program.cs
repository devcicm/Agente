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

        return "No se pudo determinar qué acción tomar.";
    }

    private async Task<ReasoningStep> GenerateReasoningStepAsync(string userInput, CancellationToken cancellationToken)
    {
        var prompt = BuildReasoningPrompt(userInput);

        // ✅ USAR COMPLETIONS API en lugar de Chat Completions
        var request = new CompletionRequest
        {
            Model = ModelId,
            Prompt = prompt,
            Temperature = 0.1,
            MaxTokens = 300,
            Stop = new List<string> { "\n\n", "```" }
        };

        try
        {
            var response = await _llm.CreateCompletionAsync(request, cancellationToken);
            var content = response.Choices.First().Text;
            return ParseReasoningResponse(content);
        }
        catch (Exception ex)
        {
            // Fallback inteligente basado en el input
            return CreateIntelligentFallback(userInput);
        }
    }

    private string BuildReasoningPrompt(string userInput)
    {
        var toolsList = string.Join(", ", _tools.Keys);

        return $$"""
    Eres un asistente de CLI. El usuario dijo: "{{userInput}}"

    Herramientas disponibles: {{toolsList}}
    Directorio actual: {{_context.CurrentDirectory}}

    Responde SOLO con JSON válido:

    {
        "thought": "breve análisis",
        "action": "qué hacer",
        "tool_name": "herramienta o null",
        "tool_arguments": {}
    }

    Si es un saludo o conversación, usa "tool_name": null.
    """;
    }

    private ReasoningStep CreateIntelligentFallback(string input)
    {
        input = input.ToLowerInvariant();

        // Detectar tipo de solicitud
        if (input.Contains("listar") || input.Contains("archivos") || input.Contains("directorio") ||
            input.Contains("carpeta") || input.Contains("qué hay") || input.Contains("muestra") ||
            input.Contains("mostrar") || input.Contains("contenido"))
        {
            return new ReasoningStep
            {
                Thought = "Usuario quiere ver archivos del directorio",
                Action = "Listando contenido del directorio",
                ToolName = "list_directory",
                ToolArguments = JsonDocument.Parse("""{"path": "."}""").RootElement
            };
        }
        else if (input.Contains("leer") && input.Contains("archivo"))
        {
            return new ReasoningStep
            {
                Thought = "Usuario quiere leer un archivo",
                Action = "Buscando archivos para leer",
                ToolName = "search_files",
                ToolArguments = JsonDocument.Parse("""{"pattern": "*.txt"}""").RootElement
            };
        }
        else if (IsConversationalInput(input))
        {
            return new ReasoningStep
            {
                Thought = "Es una conversación, no requiere acción",
                Action = "Respondiendo conversación",
                ToolName = null,
                ToolArguments = null
            };
        }
        else
        {
            // Por defecto, explorar
            return new ReasoningStep
            {
                Thought = "Explorando entorno para entender mejor",
                Action = "Listando directorio actual",
                ToolName = "list_directory",
                ToolArguments = JsonDocument.Parse("""{"path": "."}""").RootElement
            };
        }
    }

    private bool IsConversationalInput(string input)
    {
        var lower = input.ToLowerInvariant();
        return lower.Contains("hola") || lower.Contains("adiós") || lower.Contains("gracias") ||
               lower.Contains("cómo estás") || lower.Contains("qué tal") || lower.Contains("buenos días") ||
               lower.Contains("buenas tardes") || lower.Contains("buenas noches") ||
               lower.Contains("hi") || lower.Contains("hello") || lower.Contains("bye");
    }

    private string GenerateConversationalResponse(string input)
    {
        var lower = input.ToLowerInvariant();

        if (lower.Contains("hola") || lower.Contains("hi") || lower.Contains("hello"))
            return "¡Hola! Soy tu asistente CLI. ¿En qué puedo ayudarte?";
        if (lower.Contains("cómo estás") || lower.Contains("qué tal"))
            return "¡Estoy funcionando bien! Listo para ayudarte con tareas de CLI.";
        if (lower.Contains("gracias"))
            return "¡De nada! Estoy aquí para ayudarte.";
        if (lower.Contains("adiós") || lower.Contains("chao") || lower.Contains("bye"))
            return "¡Hasta luego! Vuelve si necesitas más ayuda.";

        return "¡Hola! ¿En qué puedo asistirte?";
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
                        _context.CurrentDirectory = pathProp.GetString() ?? _context.CurrentDirectory;
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
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                doc.RootElement.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("items", out var items))
                {
                    var fileList = items.EnumerateArray()
                        .Select(item =>
                            item.TryGetProperty("name", out var name) ? name.GetString() : "?")
                        .Where(name => !string.IsNullOrEmpty(name))
                        .ToList();

                    return $"Encontré {fileList.Count} elementos:\n" +
                           string.Join("\n", fileList.Take(10)) +
                           (fileList.Count > 10 ? $"\n... y {fileList.Count - 10} más" : "");
                }

                return "Operación completada exitosamente.";
            }

            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                return $"Error: {error.GetString()}";
            }
        }
        catch
        {
            // Si no se puede parsear, devolver el resultado original
        }

        return result.Length > 200 ? result.Substring(0, 200) + "..." : result;
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

                return new ReasoningStep
                {
                    Thought = root.TryGetProperty("thought", out var thought) ? thought.GetString() ?? "Pensando..." : "Pensando...",
                    Action = root.TryGetProperty("action", out var action) ? action.GetString() ?? "" : "",
                    ToolName = root.TryGetProperty("tool_name", out var tool) ? tool.GetString() : null,
                    ToolArguments = root.TryGetProperty("tool_arguments", out var args) ? args.Clone() : JsonDocument.Parse("{}").RootElement
                };
            }
        }
        catch (JsonException)
        {
            // Si no es JSON válido, buscar patrones simples
            if (response.Contains("list_directory") || response.ToLower().Contains("listar"))
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

        // Fallback total
        return new ReasoningStep
        {
            Thought = response.Length > 100 ? response.Substring(0, 100) + "..." : response,
            Action = "Acción por defecto",
            ToolName = "list_directory",
            ToolArguments = JsonDocument.Parse("""{"path": "."}""").RootElement
        };
    }
}

// Session de Reasoning
internal sealed class ReasoningSession
{
    public string OriginalInput { get; }
    public List<ReasoningStep> Steps { get; } = new();
    public AgentContext Context { get; }

    public ReasoningSession(string input, AgentContext context)
    {
        OriginalInput = input;
        Context = context;
    }

    public void AddStep(ReasoningStep step, string observation)
    {
        step.Observation = observation;
        Steps.Add(step);
    }

    public void AddObservation(string observation)
    {
        if (Steps.Count > 0)
        {
            Steps[^1].Observation = observation;
        }
    }
}

internal record ReasoningStep
{
    public string Thought { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public JsonElement? ToolArguments { get; set; }
    public string Observation { get; set; } = string.Empty;
}

// Contexto del Agente
internal sealed class AgentContext
{
    public string CurrentDirectory { get; set; } = Directory.GetCurrentDirectory();
    public string? CurrentDatabase { get; set; }
    public string? DatabaseConnectionString { get; set; }
    public List<string> RecentFiles { get; } = new();
    public Stack<string> DirectoryStack { get; } = new();
    public Dictionary<string, string> EnvironmentVars { get; } = new();
}

// ✅ NUEVO: Cliente LM Studio con Completions API
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

    // ✅ NUEVO: Usar Completions API en lugar de Chat Completions
    public async Task<CompletionResponse> CreateCompletionAsync(CompletionRequest request, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(request, _jsonOptions);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/completions")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await _http.SendAsync(httpRequest, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"LLM error: {(int)response.StatusCode} - {raw}");

        var completion = JsonSerializer.Deserialize<CompletionResponse>(raw, _jsonOptions);
        return completion ?? throw new InvalidOperationException("Respuesta del LLM vacía o inválida.");
    }
}

// ✅ NUEVOS RECORDS para Completions API
internal record CompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 0.1;

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("stop")]
    public List<string>? Stop { get; init; } = new() { "\n", "```" };
}

internal record CompletionResponse
{
    [JsonPropertyName("choices")]
    public required List<CompletionChoice> Choices { get; init; }
}

internal record CompletionChoice
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

// UI Mejorada
internal static class Ui
{
    public static void Banner(string modelId, string baseUrl)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("🚀 Agente CLI Avanzado (Claude/Codex Style)");
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

// Registry de Herramientas Completas
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

// Interfaces y herramientas base
internal interface ITool
{
    string Name { get; }
    ToolDefinition Definition { get; }
    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default);
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

// Implementaciones de herramientas específicas
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
                    path = new { type = "string", description = "Ruta del archivo" },
                    encoding = new { type = "string", description = "Codificación (utf-8, ascii, etc.)" }
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
            var encoding = GetEncoding(ToolHelpers.GetString(arguments, "encoding"));
            var content = await File.ReadAllTextAsync(path, encoding, cancellationToken);
            return ToolResult.Success(new { path, content, size = content.Length });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error leyendo archivo: {ex.Message}");
        }
    }

    private static Encoding GetEncoding(string? encoding) => encoding?.ToLowerInvariant() switch
    {
        "utf-8" => Encoding.UTF8,
        "ascii" => Encoding.ASCII,
        "unicode" => Encoding.Unicode,
        _ => Encoding.UTF8
    };
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
                    content = new { type = "string", description = "Contenido a escribir" },
                    append = new { type = "boolean", description = "Agregar al final" },
                    encoding = new { type = "string", description = "Codificación" }
                },
                required = new[] { "path", "content" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolHelpers.GetString(arguments, "path");
        var content = ToolHelpers.GetString(arguments, "content");
        var append = ToolHelpers.GetBool(arguments, "append");

        if (string.IsNullOrWhiteSpace(path))
            return ToolResult.Error("Falta el parámetro 'path'");

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var encoding = GetEncoding(ToolHelpers.GetString(arguments, "encoding"));
            if (append)
                await File.AppendAllTextAsync(path, content, encoding, cancellationToken);
            else
                await File.WriteAllTextAsync(path, content, encoding, cancellationToken);

            return ToolResult.Success(new { path, action = append ? "append" : "write" });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error escribiendo archivo: {ex.Message}");
        }
    }

    private static Encoding GetEncoding(string? encoding) => encoding?.ToLowerInvariant() switch
    {
        "utf-8" => Encoding.UTF8,
        "ascii" => Encoding.ASCII,
        "unicode" => Encoding.Unicode,
        _ => Encoding.UTF8
    };
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
                    path = new { type = "string", description = "Ruta del directorio" },
                    recursive = new { type = "boolean", description = "Búsqueda recursiva" },
                    pattern = new { type = "string", description = "Patrón de búsqueda" }
                }
            }
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolHelpers.GetString(arguments, "path") ?? ".";
        var recursive = ToolHelpers.GetBool(arguments, "recursive");
        var pattern = ToolHelpers.GetString(arguments, "pattern") ?? "*";

        if (!Directory.Exists(path))
            return Task.FromResult(ToolResult.Error($"Directorio no encontrado: {path}"));

        try
        {
            var options = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // Crear una lista de archivos
            var files = Directory.EnumerateFiles(path, pattern, options)
                .Select(f => new {
                    name = Path.GetFileName(f),
                    path = f,
                    type = "file",
                    size = new FileInfo(f).Length
                }).ToList();

            // Crear una lista de directorios
            var directories = Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly)
                .Select(d => new {
                    name = Path.GetFileName(d),
                    path = d,
                    type = "directory",
                    size = 0L // Agregar tamaño 0 para directorios
                }).ToList();

            // Combinar ambas listas
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
            Description = "Busca archivos por contenido o nombre",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Ruta de búsqueda" },
                    pattern = new { type = "string", description = "Patrón de nombre" },
                    content = new { type = "string", description = "Texto a buscar en contenido" },
                    recursive = new { type = "boolean", description = "Búsqueda recursiva" }
                }
            }
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolHelpers.GetString(arguments, "path") ?? ".";
        var pattern = ToolHelpers.GetString(arguments, "pattern") ?? "*";
        var content = ToolHelpers.GetString(arguments, "content");
        var recursive = ToolHelpers.GetBool(arguments, "recursive");

        if (!Directory.Exists(path))
            return Task.FromResult(ToolResult.Error($"Directorio no encontrado: {path}"));

        try
        {
            var options = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(path, pattern, options);

            if (!string.IsNullOrEmpty(content))
            {
                files = files.Where(f =>
                {
                    try
                    {
                        var fileContent = File.ReadAllText(f);
                        return fileContent.Contains(content, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });
            }

            var results = files.Select(f => new
            {
                path = f,
                name = Path.GetFileName(f),
                size = new FileInfo(f).Length,
                modified = File.GetLastWriteTime(f)
            }).ToList();

            return Task.FromResult(ToolResult.Success(new { path, pattern, content, results }));
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
            Description = "Ejecuta comandos de shell (PowerShell/Bash)",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    command = new { type = "string", description = "Comando a ejecutar" },
                    working_directory = new { type = "string", description = "Directorio de trabajo" }
                },
                required = new[] { "command" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var command = ToolHelpers.GetString(arguments, "command");
        var workingDir = ToolHelpers.GetString(arguments, "working_directory");

        if (string.IsNullOrWhiteSpace(command))
            return ToolResult.Error("Falta el parámetro 'command'");

        try
        {
            var fileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash";
            var args = OperatingSystem.IsWindows() ? $"-NoProfile -Command \"{command}\"" : $"-c \"{command}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory(),
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
            Description = "Ejecuta comandos SQL en la base de datos",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    database = new { type = "string", description = "Nombre de la base de datos" },
                    sql = new { type = "string", description = "Comando SQL a ejecutar" },
                    parameters = new { type = "object", description = "Parámetros para el comando" }
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
        var database = ToolHelpers.GetString(arguments, "database");

        if (string.IsNullOrWhiteSpace(sql))
            return ToolResult.Error("Falta el parámetro 'sql'");

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(database))
            {
                await using var useDb = new SqlCommand($"USE [{database}]", connection);
                await useDb.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new SqlCommand(sql, connection);

            // Agregar parámetros si existen
            if (arguments.TryGetProperty("parameters", out var paramsElement) && paramsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var param in paramsElement.EnumerateObject())
                {
                    command.Parameters.AddWithValue(param.Name, param.Value.ToString() ?? "");
                }
            }

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
            Description = "Lista todas las bases de datos disponibles",
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
            Description = "Lista tablas en una base de datos",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    database = new { type = "string", description = "Nombre de la base de datos" }
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
                    method = new { type = "string", description = "Método HTTP", @enum = new[] { "GET", "POST", "PUT", "DELETE", "PATCH" } },
                    url = new { type = "string", description = "URL destino" },
                    headers = new { type = "object", description = "Headers HTTP" },
                    body = new { type = "string", description = "Cuerpo de la petición" }
                },
                required = new[] { "url" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var method = ToolHelpers.GetString(arguments, "method") ?? "GET";
        var url = ToolHelpers.GetString(arguments, "url");
        var body = ToolHelpers.GetString(arguments, "body");

        if (string.IsNullOrWhiteSpace(url))
            return ToolResult.Error("Falta el parámetro 'url'");

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);

            // Headers
            if (arguments.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
            {
                foreach (var header in headers.EnumerateObject())
                {
                    request.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
                }
            }

            // Body
            if (!string.IsNullOrWhiteSpace(body))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            var response = await _http.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            return ToolResult.Success(new
            {
                status = (int)response.StatusCode,
                headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)),
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
            Description = "Busca información en la web (usando DuckDuckGo u otros)",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Término de búsqueda" },
                    max_results = new { type = "number", description = "Número máximo de resultados" }
                },
                required = new[] { "query" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var query = ToolHelpers.GetString(arguments, "query");
        var maxResults = ToolHelpers.GetInt(arguments, "max_results") ?? 5;

        if (string.IsNullOrWhiteSpace(query))
            return ToolResult.Error("Falta el parámetro 'query'");

        try
        {
            // Usar DuckDuckGo Instant Answer API
            var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1";

            var response = await _http.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<DuckDuckGoResponse>(response);

            var results = new List<object>();

            if (!string.IsNullOrEmpty(result?.AbstractText))
            {
                results.Add(new
                {
                    title = result.Heading ?? "Resumen",
                    content = result.AbstractText,
                    url = result.AbstractURL
                });
            }

            if (result?.RelatedTopics != null)
            {
                results.AddRange(result.RelatedTopics
                    .Where(t => !string.IsNullOrEmpty(t.Text))
                    .Take(maxResults - 1)
                    .Select(t => new
                    {
                        title = t.Text?.Split(' ').Take(5).Aggregate((a, b) => a + " " + b) ?? "Resultado",
                        content = t.Text,
                        url = t.FirstURL
                    }));
            }

            return ToolResult.Success(new { query, results });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error en búsqueda web: {ex.Message}");
        }
    }

    private record DuckDuckGoResponse(
        string? AbstractText,
        string? AbstractURL,
        string? Heading,
        List<DuckDuckGoTopic>? RelatedTopics);

    private record DuckDuckGoTopic(string? Text, string? FirstURL);
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
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Filtrar por nombre" }
                }
            }
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var nameFilter = ToolHelpers.GetString(arguments, "name");

        try
        {
            var processes = Process.GetProcesses()
                .Where(p => string.IsNullOrEmpty(nameFilter) ||
                           p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.WorkingSet64)
                .Take(50)
                .Select(p => new
                {
                    id = p.Id,
                    name = p.ProcessName,
                    memory = p.WorkingSet64 / 1024 / 1024,
                    startTime = TryGetStartTime(p)
                })
                .ToList();

            return Task.FromResult(ToolResult.Success(new { processes }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error listando procesos: {ex.Message}"));
        }
    }

    private static string? TryGetStartTime(Process process)
    {
        try { return process.StartTime.ToString("yyyy-MM-dd HH:mm:ss"); }
        catch { return null; }
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
                    path = new { type = "string", description = "Ruta del directorio o archivo" },
                    language = new { type = "string", description = "Lenguaje de programación" }
                }
            }
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolHelpers.GetString(arguments, "path") ?? ".";
        var language = ToolHelpers.GetString(arguments, "language");

        try
        {
            var codeFiles = GetCodeFiles(path, language);
            var analysis = AnalyzeCodeStructure(codeFiles);

            return Task.FromResult(ToolResult.Success(new { path, language, analysis }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error analizando código: {ex.Message}"));
        }
    }

    private static List<string> GetCodeFiles(string path, string? language)
    {
        var patterns = language?.ToLowerInvariant() switch
        {
            "c#" => new[] { "*.cs" },
            "python" => new[] { "*.py" },
            "javascript" => new[] { "*.js", "*.ts", "*.jsx", "*.tsx" },
            "java" => new[] { "*.java" },
            "php" => new[] { "*.php" },
            "ruby" => new[] { "*.rb" },
            "go" => new[] { "*.go" },
            "rust" => new[] { "*.rs" },
            _ => new[] { "*.cs", "*.py", "*.js", "*.ts", "*.java", "*.php", "*.rb", "*.go", "*.rs", "*.cpp", "*.h", "*.c", "*.hpp" }
        };

        return patterns.SelectMany(pattern =>
            Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories))
            .Take(100) // Limitar para no sobrecargar
            .ToList();
    }

    private static object AnalyzeCodeStructure(List<string> files)
    {
        return new
        {
            totalFiles = files.Count,
            fileTypes = files.GroupBy(Path.GetExtension)
                .ToDictionary(g => g.Key, g => g.Count()),
            largestFiles = files.Select(f => new {
                file = f,
                size = new FileInfo(f).Length
            })
            .OrderByDescending(f => f.size)
            .Take(10)
            .ToList()
        };
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
                    pattern = new { type = "string", description = "Texto o patrón a buscar" },
                    file_types = new { type = "string", description = "Tipos de archivo" }
                },
                required = new[] { "pattern" }
            }
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolHelpers.GetString(arguments, "path") ?? ".";
        var pattern = ToolHelpers.GetString(arguments, "pattern");
        var fileTypes = ToolHelpers.GetString(arguments, "file_types");

        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult(ToolResult.Error("Falta el parámetro 'pattern'"));

        try
        {
            var codeFiles = GetCodeFiles(path, fileTypes);
            var results = new List<object>();

            foreach (var file in codeFiles.Take(50)) // Limitar búsqueda
            {
                try
                {
                    var content = File.ReadAllText(file);
                    if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        var lines = content.Split('\n')
                            .Select((line, index) => new { line, number = index + 1 })
                            .Where(l => l.line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                            .Take(5)
                            .ToList();

                        results.Add(new
                        {
                            file,
                            matches = lines.Count,
                            snippets = lines.Select(l => new { line = l.number, text = l.line.Trim() })
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

    private static List<string> GetCodeFiles(string path, string? fileTypes)
    {
        var patterns = string.IsNullOrEmpty(fileTypes)
            ? new[] { "*.cs", "*.py", "*.js", "*.ts", "*.java", "*.php", "*.rb", "*.go", "*.rs", "*.cpp", "*.h", "*.c", "*.hpp", "*.html", "*.css", "*.xml", "*.json" }
            : fileTypes.Split(',').Select(ft => ft.Trim().StartsWith("*") ? ft.Trim() : $"*.{ft.Trim()}").ToArray();

        return patterns.SelectMany(pattern =>
            Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories))
            .Distinct()
            .Take(200) // Limitar para no sobrecargar
            .ToList();
    }
}

// Configuración de base de datos
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

// ❌ ELIMINADO: Todos los records de Chat Completions
// ✅ MANTENIDO: Solo los records de Completions API

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
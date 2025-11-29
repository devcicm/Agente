using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentCli;

/// <summary>
/// Servidor HTTP simple para exponer el agente vía REST API
/// Útil para debugging y pruebas con curl
/// </summary>
internal sealed class AgentHttpServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly Agent _agent;
    private readonly CancellationTokenSource _cts;
    private Task? _serverTask;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public AgentHttpServer(Agent agent, string prefix = "http://localhost:5000/")
    {
        _agent = agent;
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _cts = new CancellationTokenSource();

        Ui.Info($"Servidor HTTP configurado en: {prefix}");
    }

    public void Start()
    {
        _listener.Start();
        _serverTask = Task.Run(async () => await HandleRequestsAsync(_cts.Token));

        Ui.Success("Servidor HTTP iniciado");
        Ui.Info("Endpoints disponibles:");
        Ui.Info("  POST /agent/execute - Ejecuta una consulta al agente");
        Ui.Info("  GET  /agent/status  - Estado del agente");
        Ui.Info("  GET  /agent/tools   - Lista de herramientas disponibles");
        Ui.Info("  POST /debug/llm     - Prueba comunicación directa con LM Studio");
    }

    private async Task HandleRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(async () => await ProcessRequestAsync(context, cancellationToken), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Ui.Error($"Error en servidor HTTP: {ex.Message}");
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            Ui.Debug($"Request: {request.HttpMethod} {request.Url?.PathAndQuery}");

            var path = request.Url?.AbsolutePath ?? "/";
            var method = request.HttpMethod;

            object? result = null;

            if (method == "POST" && path == "/agent/execute")
            {
                result = await HandleAgentExecuteAsync(request, cancellationToken);
            }
            else if (method == "GET" && path == "/agent/status")
            {
                result = HandleAgentStatus();
            }
            else if (method == "GET" && path == "/agent/tools")
            {
                result = HandleAgentTools();
            }
            else if (method == "POST" && path == "/debug/llm")
            {
                result = await HandleDebugLlmAsync(request, cancellationToken);
            }
            else
            {
                result = new
                {
                    error = "Endpoint no encontrado",
                    availableEndpoints = new[]
                    {
                        "POST /agent/execute",
                        "GET  /agent/status",
                        "GET  /agent/tools",
                        "POST /debug/llm"
                    }
                };
                response.StatusCode = 404;
            }

            await SendJsonResponseAsync(response, result);
        }
        catch (Exception ex)
        {
            Ui.Error($"Error procesando request: {ex.Message}");
            await SendJsonResponseAsync(response, new
            {
                error = ex.Message,
                type = ex.GetType().Name
            }, 500);
        }
    }

    private async Task<object> HandleAgentExecuteAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await reader.ReadToEndAsync();

        Ui.Debug($"Request body: {body}");

        var executeRequest = JsonSerializer.Deserialize<AgentExecuteRequest>(body, _jsonOptions);

        if (string.IsNullOrWhiteSpace(executeRequest?.Query))
        {
            return new { error = "Query is required", example = new { query = "lista los archivos" } };
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var thoughts = new List<string>();
        var actions = new List<string>();

        Ui.Info($"Ejecutando: {executeRequest.Query}");

        var result = await _agent.ExecuteWithReasoningAsync(
            executeRequest.Query,
            thought => thoughts.Add(thought),
            action => actions.Add(action),
            cancellationToken
        );

        sw.Stop();

        Ui.Success($"Ejecutado en {sw.ElapsedMilliseconds}ms");

        return new
        {
            success = true,
            query = executeRequest.Query,
            result = result,
            thoughts = thoughts,
            actions = actions,
            durationMs = sw.ElapsedMilliseconds,
            timestamp = DateTime.UtcNow
        };
    }

    private object HandleAgentStatus()
    {
        return new
        {
            status = "running",
            model = _agent.ModelId,
            baseUrl = _agent.BaseUrl,
            streaming = _agent.UseStreaming,
            debugMode = Ui.IsDebugEnabled(),
            tools = _agent.GetToolNames(),
            timestamp = DateTime.UtcNow
        };
    }

    private object HandleAgentTools()
    {
        return new
        {
            tools = _agent.GetToolNames(),
            count = _agent.GetToolNames().Count,
            timestamp = DateTime.UtcNow
        };
    }

    private async Task<object> HandleDebugLlmAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await reader.ReadToEndAsync();

        Ui.Debug($"Debug LLM request: {body}");

        var debugRequest = JsonSerializer.Deserialize<DebugLlmRequest>(body, _jsonOptions);

        if (string.IsNullOrWhiteSpace(debugRequest?.Input))
        {
            return new { error = "Input is required", example = new { input = "test prompt" } };
        }

        Ui.Info($"Debug LLM: {debugRequest.Input}");

        try
        {
            var llmResponse = await _agent.DebugLlmCallAsync(debugRequest.Input, cancellationToken);

            return new
            {
                success = true,
                input = debugRequest.Input,
                response = llmResponse,
                timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace,
                timestamp = DateTime.UtcNow
            };
        }
    }

    private async Task SendJsonResponseAsync(HttpListenerResponse response, object data, int statusCode = 200)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers.Add("Access-Control-Allow-Origin", "*");

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        var buffer = Encoding.UTF8.GetBytes(json);

        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.OutputStream.Close();

        Ui.Debug($"Response: {statusCode} - {json.Substring(0, Math.Min(200, json.Length))}...");
    }

    public void Stop()
    {
        _cts.Cancel();
        _listener.Stop();
        _serverTask?.Wait(TimeSpan.FromSeconds(5));
        Ui.Info("Servidor HTTP detenido");
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts.Dispose();
    }
}

internal record AgentExecuteRequest
{
    [JsonPropertyName("query")]
    public string? Query { get; init; }
}

internal record DebugLlmRequest
{
    [JsonPropertyName("input")]
    public string? Input { get; init; }
}

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace EngineConsole
{
    public class ThinkingResponse
    {
        public string Thinking { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public bool HasThinking => !string.IsNullOrWhiteSpace(Thinking);
        public bool HasResponse => !string.IsNullOrWhiteSpace(Response);
    }

    public class ModelInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public override string ToString() => $"{Name} ({Id})";
    }

    public class TestQuestion
    {
        public string Question { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }

    internal static class Engine
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(120)
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        // Logger global
        private static StreamWriter? _logWriter;
        private static bool _loggingEnabled = false;
        private static readonly object _logLock = new object();

        public static void InitializeLogger()
        {
            if (_loggingEnabled && _logWriter == null)
            {
                var logFile = $"llm_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                _logWriter = new StreamWriter(logFile, append: true, Encoding.UTF8);
                _logWriter.AutoFlush = true;
                
                LogToFile("=== LOG INICIADO ===");
                LogToFile($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                LogToFile($"Directorio: {Environment.CurrentDirectory}");
                LogToFile("====================\n");
            }
        }

        public static void EnableLogging(bool enable)
        {
            _loggingEnabled = enable;
            if (enable)
            {
                InitializeLogger();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Logging activado - Los logs se guardan en archivo .txt");
                Console.ResetColor();
            }
            else
            {
                _logWriter?.Close();
                _logWriter = null;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("✗ Logging desactivado");
                Console.ResetColor();
            }
        }

        public static void LogToFile(string message)
        {
            if (!_loggingEnabled || _logWriter == null) return;

            lock (_logLock)
            {
                try
                {
                    _logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error escribiendo log: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }

        public static void LogRequest(string model, string input)
        {
            if (!_loggingEnabled) return;
            
            var logMessage = $"[REQUEST] Modelo: {model}\n" +
                           $"[INPUT] {input}\n" +
                           $"[TIMESTAMP] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                           "---";
            LogToFile(logMessage);
        }

        public static void LogResponse(string model, string rawResponse, ThinkingResponse? parsedResponse = null)
        {
            if (!_loggingEnabled) return;

            var logMessage = $"[RESPONSE] Modelo: {model}\n" +
                           $"[RAW DATA]\n{rawResponse}\n";

            if (parsedResponse != null)
            {
                if (parsedResponse.HasThinking)
                {
                    logMessage += $"[THINKING]\n{parsedResponse.Thinking}\n";
                }
                if (parsedResponse.HasResponse)
                {
                    logMessage += $"[FINAL RESPONSE]\n{parsedResponse.Response}\n";
                }
            }
            
            logMessage += "--- END RESPONSE ---";
            LogToFile(logMessage);
        }

        public static async Task<List<ModelInfo>> GetModelsAsync(string baseUrl)
        {
            var models = new List<ModelInfo>();
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("=== Obteniendo modelos disponibles ===");
                Console.ResetColor();

                LogToFile($"Obteniendo modelos de: {baseUrl}");

                var response = await Http.GetAsync($"{baseUrl.TrimEnd('/')}/v1/models");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    LogToFile($"Respuesta modelos RAW: {json}");
                    
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(json);
                            
                            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var modelElement in data.EnumerateArray())
                                {
                                    var model = new ModelInfo();
                                    
                                    if (modelElement.TryGetProperty("id", out var id))
                                        model.Id = id.GetString() ?? string.Empty;
                                    
                                    if (modelElement.TryGetProperty("name", out var name))
                                        model.Name = name.GetString() ?? string.Empty;
                                    else
                                        model.Name = model.Id;
                                    
                                    if (modelElement.TryGetProperty("description", out var description))
                                        model.Description = description.GetString() ?? string.Empty;

                                    if (!string.IsNullOrEmpty(model.Id))
                                        models.Add(model);
                                }
                            }
                            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var modelElement in doc.RootElement.EnumerateArray())
                                {
                                    var model = new ModelInfo();
                                    
                                    if (modelElement.TryGetProperty("id", out var id))
                                        model.Id = id.GetString() ?? string.Empty;
                                    
                                    if (modelElement.TryGetProperty("name", out var name))
                                        model.Name = name.GetString() ?? string.Empty;
                                    else
                                        model.Name = model.Id;
                                    
                                    if (modelElement.TryGetProperty("description", out var description))
                                        model.Description = description.GetString() ?? string.Empty;

                                    if (!string.IsNullOrEmpty(model.Id))
                                        models.Add(model);
                                }
                            }
                            
                            LogToFile($"Modelos encontrados: {models.Count}");
                        }
                        catch (JsonException ex)
                        {
                            LogToFile($"ERROR Parseando modelos: {ex.Message}");
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Error parseando respuesta de modelos: {ex.Message}");
                            Console.ResetColor();
                        }
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    LogToFile($"ERROR Obteniendo modelos: {response.StatusCode} - {errorContent}");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error obteniendo modelos: {response.StatusCode}");
                    if (!string.IsNullOrEmpty(errorContent))
                    {
                        Console.WriteLine($"Detalles: {errorContent}");
                    }
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                LogToFile($"EXCEPCIÓN Obteniendo modelos: {ex.Message}");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error al obtener modelos: {ex.Message}");
                Console.ResetColor();
            }

            return models;
        }

        public static string ExtractFirstText(string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                {
                    foreach (var msg in output.EnumerateArray())
                    {
                        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var c in content.EnumerateArray())
                            {
                                if (c.TryGetProperty("type", out var type) &&
                                    type.GetString() == "output_text" &&
                                    c.TryGetProperty("text", out var textProp))
                                {
                                    return textProp.GetString() ?? raw;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"[WARN] Error parseando respuesta: {ex.Message}");
                Console.ResetColor();
            }
            return raw;
        }

        public static async Task<string> InvokeAsync(string baseUrl, string modelId, string input)
        {
            var payload = new
            {
                model = modelId,
                input,
                stream = false
            };

            LogRequest(modelId, input);

            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/v1/responses")
            {
                Content = content
            };

            using var response = await Http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            
            LogResponse(modelId, raw);
            return raw;
        }

        public static async Task<ThinkingResponse> StreamAsync(string baseUrl, string modelId, string input, CancellationToken cancellationToken, bool showDebug = false)
        {
            var result = new ThinkingResponse();
            var payload = new
            {
                model = modelId,
                input,
                stream = true
            };

            LogRequest(modelId, input);

            if (showDebug)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[DEBUG] Enviando request streaming a: {baseUrl}/v1/responses");
                Console.WriteLine($"[DEBUG] Model: {modelId}");
                Console.ResetColor();
            }

            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/v1/responses")
            {
                Content = content
            };

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                LogToFile($"ERROR HTTP: {response.StatusCode} - {errorContent}");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] HTTP {response.StatusCode}: {errorContent}");
                Console.ResetColor();
                return result;
            }
            
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            var fullContent = new StringBuilder();
            var rawEvents = new List<string>();

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync();
                
                if (string.IsNullOrWhiteSpace(line)) continue;

                rawEvents.Add(line);
                LogToFile($"[STREAM RAW] {EscapeControlCharacters(line)}");

                if (showDebug)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[RAW EVENT] {EscapeControlCharacters(line)}");
                    Console.ResetColor();
                }

                if (!line.StartsWith("data:")) continue;

                var jsonData = line.Substring("data:".Length).Trim();
                if (jsonData == "[DONE]") 
                {
                    LogToFile("[STREAM] Señal [DONE] recibida");
                    if (showDebug)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("[DEBUG] Señal [DONE] recibida");
                        Console.ResetColor();
                    }
                    break;
                }

                try
                {
                    using var doc = JsonDocument.Parse(jsonData);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("type", out var typeProp))
                    {
                        var eventType = typeProp.GetString();
                        
                        if (eventType == "response.output_text.delta" && root.TryGetProperty("delta", out var delta))
                        {
                            var chunk = delta.GetString();
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                var processedChunk = ProcessSpecialCharacters(chunk);
                                fullContent.Append(processedChunk);
                                Console.Write(processedChunk);
                            }
                        }
                        else if (showDebug)
                        {
                            Console.ForegroundColor = ConsoleColor.Blue;
                            Console.WriteLine($"[DEBUG] Evento tipo: {eventType}");
                            Console.ResetColor();
                        }
                    }

                    if (showDebug)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine("[DEBUG JSON COMPLETO]:");
                        Console.WriteLine(FormatJson(jsonData));
                        Console.ResetColor();
                    }
                }
                catch (JsonException)
                {
                    if (showDebug)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"[DEBUG JSON ERROR] Error parseando JSON");
                        Console.WriteLine($"[DEBUG JSON DATA] {jsonData}");
                        Console.ResetColor();
                    }
                    continue;
                }
                catch (Exception ex)
                {
                    if (showDebug)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[ERROR] Error procesando chunk: {ex.Message}");
                        Console.ResetColor();
                    }
                    continue;
                }
            }

            var finalContent = fullContent.ToString();
            LogToFile($"[STREAM COMPLETO] {EscapeControlCharacters(finalContent)}");

            if (showDebug)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"\n[DEBUG CONTENIDO COMPLETO ACUMULADO]:");
                Console.WriteLine(EscapeControlCharacters(finalContent));
                Console.ResetColor();
            }

            ProcessFinalContent(finalContent, result);
            LogResponse(modelId, finalContent, result);
            
            Console.WriteLine();
            return result;
        }

        private static string EscapeControlCharacters(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            return Regex.Replace(input, @"[\x00-\x1F\x7F]", match =>
            {
                var charCode = (int)match.Value[0];
                return charCode switch
                {
                    0 => "\\0",
                    7 => "\\a",
                    8 => "\\b",
                    9 => "\\t",
                    10 => "\\n",
                    11 => "\\v",
                    12 => "\\f",
                    13 => "\\r",
                    _ => $"\\x{charCode:X2}"
                };
            });
        }

        private static string ProcessSpecialCharacters(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var processed = Regex.Replace(input, @"\\x([0-9A-Fa-f]{2})", match =>
            {
                try
                {
                    var hex = match.Groups[1].Value;
                    var charCode = Convert.ToInt32(hex, 16);
                    return ((char)charCode).ToString();
                }
                catch
                {
                    return match.Value;
                }
            });

            processed = processed.Replace("\\n", "\n")
                                .Replace("\\r", "\r")
                                .Replace("\\t", "\t")
                                .Replace("\\\"", "\"")
                                .Replace("\\\\", "\\");

            return processed;
        }

        private static string FormatJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }
            catch
            {
                return json;
            }
        }

        private static void ProcessFinalContent(string fullContent, ThinkingResponse result)
        {
            var (thinking, response) = SimpleContentSeparation(fullContent);
            
            result.Thinking = thinking.Trim();
            result.Response = response.Trim();
        }

        private static (string thinking, string response) SimpleContentSeparation(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return (string.Empty, string.Empty);

            var transitionPatterns = new[]
            {
                @"¡Hola\s*[!]?", @"Hola[!]?\s+", @"Hola,\s+",
                @"\*\*", @"###", @"---", @"\.\s*$",
                @"[.!?]\s+[A-ZÁÉÍÓÚÜ]",
                @"\b(La|El|Los|Las|Un|Una)\s+[A-Z]",
                @"\b(Marco|Aurelio|Fórmula|Agua|H₂?O|H2O)\b"
            };

            int bestTransition = -1;
            
            foreach (var pattern in transitionPatterns)
            {
                try
                {
                    var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        var position = match.Index;
                        
                        if (IsValidTransitionPoint(content, position))
                        {
                            if (bestTransition == -1 || position < bestTransition)
                            {
                                bestTransition = position;
                            }
                        }
                    }
                }
                catch (ArgumentException)
                {
                    continue;
                }
            }

            if (bestTransition == -1)
            {
                bestTransition = FindLanguageTransition(content);
            }

            if (bestTransition == -1)
            {
                bestTransition = FindLastEnglishSegment(content);
            }

            if (bestTransition > 0 && bestTransition < content.Length)
            {
                var thinkingPart = content.Substring(0, bestTransition).Trim();
                var responsePart = content.Substring(bestTransition).Trim();

                if (thinkingPart.Length > 10 && responsePart.Length > 5 && 
                    IsMostlyEnglish(thinkingPart) && !IsMostlyEnglish(responsePart))
                {
                    return (thinkingPart, responsePart);
                }
            }

            return AnalyzeCompleteContent(content);
        }

        private static bool IsValidTransitionPoint(string content, int position)
        {
            if (position <= 0 || position >= content.Length) return false;

            var before = content.Substring(0, position);
            var after = content.Substring(position);

            return IsMostlyEnglish(before) && !IsMostlyEnglish(after) && after.Length > 5;
        }

        private static int FindLanguageTransition(string content)
        {
            for (int i = 50; i < content.Length - 50; i += 10)
            {
                var windowBefore = content.Substring(Math.Max(0, i - 30), Math.Min(30, i));
                var windowAfter = content.Substring(i, Math.Min(30, content.Length - i));

                if (IsMostlyEnglish(windowBefore) && !IsMostlyEnglish(windowAfter))
                {
                    return i;
                }
            }
            return -1;
        }

        private static int FindLastEnglishSegment(string content)
        {
            var sentences = Regex.Split(content, @"(?<=[.!?])\s+");
            var englishCount = 0;
            
            for (int i = 0; i < sentences.Length; i++)
            {
                if (IsMostlyEnglish(sentences[i]))
                {
                    englishCount = i + 1;
                }
                else
                {
                    break;
                }
            }

            if (englishCount > 0 && englishCount < sentences.Length)
            {
                var thinkingPart = string.Join(" ", sentences.Take(englishCount));
                return thinkingPart.Length;
            }

            return -1;
        }

        private static (string thinking, string response) AnalyzeCompleteContent(string content)
        {
            if (IsMostlyEnglish(content))
                return (content, string.Empty);

            var firstSentence = GetFirstSentence(content);
            if (!IsMostlyEnglish(firstSentence) || content.Length - firstSentence.Length < 10)
                return (string.Empty, content);

            return (content, string.Empty);
        }

        private static string GetFirstSentence(string text)
        {
            var match = Regex.Match(text, @"^.*?[.!?](?:\s|$)");
            return match.Success ? match.Value : text;
        }

        private static bool IsMostlyEnglish(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            var englishWords = new[] 
            {
                "the", "and", "ing", "to", "of", "a", "in", "is", "it", "you", "that", 
                "he", "was", "for", "on", "are", "as", "with", "his", "they", "i", "at",
                "be", "this", "have", "from", "thinking", "user", "should", "need", "consider",
                "analyze", "reasoning", "planning", "step", "context", "would", "could", "might",
                "maybe", "probably", "perhaps", "likely", "about", "because", "since", "while"
            };

            var spanishWords = new[]
            {
                "el", "la", "de", "que", "y", "en", "un", "es", "se", "no", "te", "lo",
                "le", "su", "por", "con", "una", "los", "las", "del", "al", "como", "más",
                "pero", "sus", "hola", "gracias", "por", "preguntar", "puedo", "ayudar",
                "ayudarte", "buenos", "días", "tardes", "noches", "claro", "encantado",
                "alegra", "excelente", "perfecto", "fórmula", "química", "agua", "marco",
                "aurelio", "fue", "era", "historia", "importante", "emperador", "filósofo"
            };

            var words = Regex.Matches(text.ToLower(), @"\b\w+\b");
            if (words.Count == 0) return true;

            var englishCount = words.Cast<Match>()
                .Count(match => englishWords.Contains(match.Value));

            var spanishCount = words.Cast<Match>()
                .Count(match => spanishWords.Contains(match.Value));

            return englishCount > spanishCount;
        }

        public static async Task TestLmStudioApi(string baseUrl, string modelId)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("=== Probando API de LM Studio ===");
                Console.ResetColor();
                
                try
                {
                    var healthResponse = await Http.GetAsync($"{baseUrl}/health");
                    Console.WriteLine($"Health check: {healthResponse.StatusCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Health check falló: {ex.Message}");
                }
                
                Console.WriteLine("Probando solicitud simple...");
                var testPayload = new
                {
                    model = modelId,
                    input = "Responde con 'OK' si estás funcionando correctamente",
                    stream = false
                };
                
                using var testContent = new StringContent(JsonSerializer.Serialize(testPayload, JsonOptions), Encoding.UTF8, "application/json");
                var testResponse = await Http.PostAsync($"{baseUrl}/v1/responses", testContent);
                
                if (testResponse.IsSuccessStatusCode)
                {
                    var testResult = await testResponse.Content.ReadAsStringAsync();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ Test exitoso");
                    
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("Respuesta JSON:");
                    try
                    {
                        var formattedJson = JsonSerializer.Serialize(
                            JsonDocument.Parse(testResult).RootElement,
                            new JsonSerializerOptions { WriteIndented = true }
                        );
                        Console.WriteLine(formattedJson);
                    }
                    catch
                    {
                        Console.WriteLine(testResult);
                    }
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"✗ Error en test: {testResponse.StatusCode}");
                    var errorContent = await testResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"Detalles: {errorContent}");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error en test de API: {ex.Message}");
                Console.ResetColor();
            }
        }

        // Nuevo método para ejecutar múltiples LLMs en paralelo
        public static async Task<List<(string model, ThinkingResponse result)>> RunMultipleModelsAsync(
            string baseUrl, List<string> modelIds, string input, bool useStream, bool showDebug = false)
        {
            var tasks = modelIds.Select(async modelId =>
            {
                try
                {
                    ThinkingResponse result;
                    if (useStream)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                        result = await StreamAsync(baseUrl, modelId, input, cts.Token, showDebug);
                    }
                    else
                    {
                        var raw = await InvokeAsync(baseUrl, modelId, input);
                        result = new ThinkingResponse { Response = ExtractFirstText(raw) };
                    }
                    return (modelId, result);
                }
                catch (Exception ex)
                {
                    LogToFile($"ERROR con modelo {modelId}: {ex.Message}");
                    return (modelId, new ThinkingResponse { Response = $"Error: {ex.Message}" });
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }
    }

    internal class Program
    {
        private const string DefaultModel = "gpt-oss-20b-gpt-5-reasoning-distill";
        private const string DefaultBaseUrl = "http://localhost:1234";

        // Variables para múltiples modelos
        private static List<string> _activeModels = new List<string> { DefaultModel };
        private static bool _useMultipleModels = false;

        // Variables para TTS
        private static bool _useTts = false;
        private static VibeVoiceClient? _ttsClient = null;
        private static int _ttsCounter = 1;

        // Lista de preguntas para testing
        private static readonly List<TestQuestion> _testQuestions = new()
        {
            // Razonamiento y lógica
            new TestQuestion { Category = "Razonamiento", Question = "Si tengo 3 manzanas y me das 2 más, ¿cuántas manzanas tengo en total?" },
            new TestQuestion { Category = "Razonamiento", Question = "Explica el concepto de gravedad de manera simple" },
            new TestQuestion { Category = "Razonamiento", Question = "¿Qué es más pesado: un kilo de plumas o un kilo de plomo?" },
            new TestQuestion { Category = "Razonamiento", Question = "Si todos los humanos son mortales y Sócrates es humano, ¿entonces Sócrates es mortal?" },
            new TestQuestion { Category = "Razonamiento", Question = "¿Cuál es la diferencia entre clima y tiempo atmosférico?" },
            
            // Matemáticas
            new TestQuestion { Category = "Matemáticas", Question = "Resuelve: 15 × 8 + 32 ÷ 4" },
            new TestQuestion { Category = "Matemáticas", Question = "¿Cuál es el área de un círculo con radio 5 cm?" },
            new TestQuestion { Category = "Matemáticas", Question = "Explica qué es el teorema de Pitágoras" },
            new TestQuestion { Category = "Matemáticas", Question = "¿Cuál es la fórmula para calcular el volumen de una esfera?" },
            new TestQuestion { Category = "Matemáticas", Question = "Simplifica la expresión: 3x + 2y - x + 4y" },
            
            // Ciencias
            new TestQuestion { Category = "Ciencias", Question = "¿Qué es la fotosíntesis y por qué es importante?" },
            new TestQuestion { Category = "Ciencias", Question = "Explica la diferencia entre elementos y compuestos químicos" },
            new TestQuestion { Category = "Ciencias", Question = "¿Qué es el ADN y qué función cumple?" },
            new TestQuestion { Category = "Ciencias", Question = "Nombra los planetas del sistema solar en orden" },
            new TestQuestion { Category = "Ciencias", Question = "¿Qué causa las estaciones del año en la Tierra?" },
            
            // Historia y cultura
            new TestQuestion { Category = "Historia", Question = "¿Quién fue Marco Aurelio y por qué es importante?" },
            new TestQuestion { Category = "Historia", Question = "Explica brevemente la Revolución Industrial" },
            new TestQuestion { Category = "Historia", Question = "¿Qué fue la Segunda Guerra Mundial y cuándo ocurrió?" },
            new TestQuestion { Category = "Historia", Question = "¿Quién descubrió América y en qué año?" },
            new TestQuestion { Category = "Historia", Question = "Habla sobre la civilización egipcia antigua" },
            
            // Literatura y arte
            new TestQuestion { Category = "Literatura", Question = "¿Quién escribió 'Cien años de soledad'?" },
            new TestQuestion { Category = "Literatura", Question = "Explica el concepto de realismo mágico" },
            new TestQuestion { Category = "Literatura", Question = "¿Qué es el Renacimiento en el arte?" },
            new TestQuestion { Category = "Literatura", Question = "Nombra tres obras importantes de Shakespeare" },
            new TestQuestion { Category = "Literatura", Question = "¿Quién pintó la Mona Lisa?" },
            
            // Tecnología
            new TestQuestion { Category = "Tecnología", Question = "Explica qué es la inteligencia artificial" },
            new TestQuestion { Category = "Tecnología", Question = "¿Qué es el machine learning?" },
            new TestQuestion { Category = "Tecnología", Question = "Diferencias entre Python y JavaScript" },
            new TestQuestion { Category = "Tecnología", Question = "¿Qué es blockchain y cómo funciona?" },
            new TestQuestion { Category = "Tecnología", Question = "Explica el concepto de cloud computing" },
            
            // Filosofía y ética
            new TestQuestion { Category = "Filosofía", Question = "¿Qué es el dilema del tranvía en ética?" },
            new TestQuestion { Category = "Filosofía", Question = "Explica la teoría de las formas de Platón" },
            new TestQuestion { Category = "Filosofía", Question = "¿Qué significa 'pienso, luego existo'?" },
            new TestQuestion { Category = "Filosofía", Question = "Diferencias entre ética y moral" },
            new TestQuestion { Category = "Filosofía", Question = "¿Qué es el utilitarismo?" },
            
            // Preguntas prácticas
            new TestQuestion { Category = "Práctico", Question = "¿Cómo cambiaría una rueda pinchada?" },
            new TestQuestion { Category = "Práctico", Question = "Explica cómo hacer una presentación efectiva" },
            new TestQuestion { Category = "Práctico", Question = "¿Cuáles son los pasos para resolver un conflicto?" },
            new TestQuestion { Category = "Práctico", Question = "Cómo administrar mejor el tiempo" },
            new TestQuestion { Category = "Práctico", Question = "Consejos para aprender un nuevo idioma" }
        };

        public static async Task Main(string[] args)
        {
            var baseUrl = Environment.GetEnvironmentVariable("LMSTUDIO_URL") ?? DefaultBaseUrl;
            var useStream = false;
            var showDebug = false;
            
            if (args.Length > 0)
            {
                foreach (var arg in args)
                {
                    if (arg.Equals("--stream", StringComparison.OrdinalIgnoreCase))
                        useStream = true;
                    if (arg.Equals("--debug", StringComparison.OrdinalIgnoreCase))
                        showDebug = true;
                }
            }

            await Engine.TestLmStudioApi(baseUrl, _activeModels.First());
            Console.WriteLine();

            if (args.Length > 0 && !args[0].StartsWith("--"))
            {
                var inputOnce = string.Join(" ", args.Where(a => !a.StartsWith("--")));
                await RunOnce(baseUrl, _activeModels, inputOnce, useStream, showDebug);
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==== ENGINE LLM - CLI (COMANDOS MEJORADOS) ====");
            Console.ResetColor();
            Console.WriteLine($"Modelos activos:   {string.Join(", ", _activeModels)}");
            Console.WriteLine($"Endpoint:          {baseUrl}");
            Console.WriteLine("Comandos:");
            Console.WriteLine("  /exit         - Salir del programa");
            Console.WriteLine("  /help         - Mostrar ayuda");
            Console.WriteLine("  /clear        - Limpiar pantalla");
            Console.WriteLine("  /logs on/off  - Activar/desactivar logging a archivo");
            Console.WriteLine("  /stream on/off- Activar/desactivar modo streaming (usa: /stream on tts para TTS)");
            Console.WriteLine("  /debug on/off - Mostrar/ocultar logs detallados");
            Console.WriteLine("  /test         - Ejecutar preguntas de prueba en modelos");
            Console.WriteLine("  /models [n|id]- Listar modelos (o seleccionar: /models 1)");
            Console.WriteLine("  /model <id>   - Cambiar modelo (ej: /model 1)");
            Console.WriteLine("  /multi on/off - Activar/desactivar múltiples modelos");
            Console.WriteLine("  /add <id>     - Agregar modelo a la lista activa");
            Console.WriteLine("  /remove <id>  - Remover modelo de la lista activa");
            Console.WriteLine("  /current      - Mostrar modelos activos");
            Console.WriteLine();

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("> ");
                Console.ResetColor();
                var line = Console.ReadLine();
                if (line == null) break;
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                
                var isCommand = ProcessCommand(line, ref baseUrl, ref useStream, ref showDebug);
                
                if (isCommand)
                {
                    Console.WriteLine();
                    continue;
                }

                await RunOnce(baseUrl, _activeModels, line, useStream, showDebug);
                Console.WriteLine();
            }
        }

        private static bool ProcessCommand(string line, ref string baseUrl, ref bool useStream, ref bool showDebug)
        {
            var lowerLine = line.ToLowerInvariant();
            
            if (lowerLine == "/exit")
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Saliendo del programa...");
                Console.ResetColor();
                Environment.Exit(0);
                return true;
            }
            
            if (lowerLine == "/help")
            {
                ShowHelp();
                return true;
            }
            
            if (lowerLine == "/clear")
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("==== ENGINE LLM - CLI ====");
                Console.ResetColor();
                return true;
            }
            
            if (lowerLine.StartsWith("/logs"))
            {
                var enable = line.ToLowerInvariant().Contains("on");
                Engine.EnableLogging(enable);
                return true;
            }
            
            if (lowerLine.StartsWith("/stream"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var hasTts = parts.Any(p => p.Equals("tts", StringComparison.OrdinalIgnoreCase));
                var hasOn = line.ToLowerInvariant().Contains("on");
                var hasOff = line.ToLowerInvariant().Contains("off");

                if (hasOff)
                {
                    useStream = false;
                    _useTts = false;
                    Console.WriteLine("Streaming: desactivado | TTS: desactivado");
                }
                else if (hasOn)
                {
                    useStream = true;
                    _useTts = hasTts;
                    if (_useTts)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓ Streaming + TTS: activado");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("Streaming: activado | TTS: desactivado");
                    }
                }
                else
                {
                    // Toggle
                    useStream = !useStream;
                    _useTts = false;
                    Console.WriteLine($"Streaming: {(useStream ? "activado" : "desactivado")} | TTS: desactivado");
                }
                return true;
            }
            
            if (lowerLine.StartsWith("/debug"))
            {
                showDebug = line.ToLowerInvariant().Contains("on");
                Console.WriteLine($"Debug {(showDebug ? "activado" : "desactivado")}.");
                return true;
            }
            
            if (lowerLine == "/test")
            {
                _ = RunTestSuiteAsync(baseUrl, _activeModels, useStream, showDebug);
                return true;
            }
            
            if (lowerLine.StartsWith("/models", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                {
                    _ = ListModelsAsync(baseUrl);
                    return true;
                }

                var selection = parts[1].Trim();
                if (string.IsNullOrWhiteSpace(selection))
                {
                    _ = ListModelsAsync(baseUrl);
                    return true;
                }

                // UX: permitir /models <n|id> como alias de /model <n|id>
                return ChangeModel(baseUrl, selection);
            }
            
            if (lowerLine.StartsWith("/model "))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var newModel = parts[1];
                    return ChangeModel(baseUrl, newModel);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Uso: /model <número o ID>");
                    Console.WriteLine("Ejemplo: /model 1 o /model gpt-4");
                    Console.ResetColor();
                    return true;
                }
            }
            
            if (lowerLine.StartsWith("/multi"))
            {
                _useMultipleModels = line.ToLowerInvariant().Contains("on");
                Console.WriteLine($"Múltiples modelos {(_useMultipleModels ? "activado" : "desactivado")}.");
                if (_useMultipleModels)
                {
                    Console.WriteLine($"Modelos activos: {string.Join(", ", _activeModels)}");
                }
                return true;
            }
            
            if (lowerLine.StartsWith("/add "))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var modelToAdd = parts[1];
                    AddModel(baseUrl, modelToAdd);
                }
                return true;
            }
            
            if (lowerLine.StartsWith("/remove "))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var modelToRemove = parts[1];
                    RemoveModel(modelToRemove);
                }
                return true;
            }
            
            if (lowerLine == "/current")
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Modelos activos: {string.Join(", ", _activeModels)}");
                Console.ResetColor();
                return true;
            }

            if (line.StartsWith("/"))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Comando no reconocido: {line}");
                Console.WriteLine("Escribe /help para ver los comandos disponibles.");
                Console.ResetColor();
                return true;
            }

            return false;
        }

        private static async Task ListModelsAsync(string baseUrl)
        {
            var models = await Engine.GetModelsAsync(baseUrl);
            if (models.Any())
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n=== MODELOS DISPONIBLES ({models.Count}) ===");
                Console.ResetColor();
                
                for (int i = 0; i < models.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {models[i]}");
                    if (!string.IsNullOrEmpty(models[i].Description))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"   {models[i].Description}");
                        Console.ResetColor();
                    }
                }
                
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\nUsa '/model <número o ID>' para seleccionar un modelo");
                Console.WriteLine($"Ejemplo: '/model 1' o '/model {models.First().Id}'");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("No se encontraron modelos disponibles.");
                Console.ResetColor();
            }
        }

        private static bool ChangeModel(string baseUrl, string newModel)
        {
            if (int.TryParse(newModel, out int modelNumber))
            {
                var modelsTask = Engine.GetModelsAsync(baseUrl);
                modelsTask.Wait();
                var models = modelsTask.Result;
                
                if (modelNumber > 0 && modelNumber <= models.Count)
                {
                    _activeModels = new List<string> { models[modelNumber - 1].Id };
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Modelo cambiado a: {_activeModels.First()}");
                    Console.ResetColor();
                    return true;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Número de modelo inválido. Usa /models para ver la lista.");
                    Console.ResetColor();
                    return true;
                }
            }
            else
            {
                _activeModels = new List<string> { newModel };
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Modelo cambiado a: {_activeModels.First()}");
                Console.ResetColor();
                return true;
            }
        }

        private static void AddModel(string baseUrl, string modelToAdd)
        {
            if (int.TryParse(modelToAdd, out int modelNumber))
            {
                var modelsTask = Engine.GetModelsAsync(baseUrl);
                modelsTask.Wait();
                var models = modelsTask.Result;
                
                if (modelNumber > 0 && modelNumber <= models.Count)
                {
                    var modelId = models[modelNumber - 1].Id;
                    if (!_activeModels.Contains(modelId))
                    {
                        _activeModels.Add(modelId);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"Modelo agregado: {modelId}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"El modelo {modelId} ya está en la lista activa.");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Número de modelo inválido.");
                    Console.ResetColor();
                }
            }
            else
            {
                if (!_activeModels.Contains(modelToAdd))
                {
                    _activeModels.Add(modelToAdd);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Modelo agregado: {modelToAdd}");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"El modelo {modelToAdd} ya está en la lista activa.");
                    Console.ResetColor();
                }
            }
            
            Console.WriteLine($"Modelos activos: {string.Join(", ", _activeModels)}");
        }

        private static void RemoveModel(string modelToRemove)
        {
            if (_activeModels.Count <= 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("No se puede remover el último modelo activo.");
                Console.ResetColor();
                return;
            }

            if (int.TryParse(modelToRemove, out int modelNumber) && modelNumber > 0 && modelNumber <= _activeModels.Count)
            {
                var removedModel = _activeModels[modelNumber - 1];
                _activeModels.RemoveAt(modelNumber - 1);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Modelo removido: {removedModel}");
                Console.ResetColor();
            }
            else if (_activeModels.Contains(modelToRemove))
            {
                _activeModels.Remove(modelToRemove);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Modelo removido: {modelToRemove}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Modelo no encontrado en la lista activa.");
                Console.ResetColor();
            }
            
            Console.WriteLine($"Modelos activos: {string.Join(", ", _activeModels)}");
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Comandos disponibles:");
            Console.WriteLine("  /exit         - Salir del programa");
            Console.WriteLine("  /help         - Mostrar esta ayuda");
            Console.WriteLine("  /clear        - Limpiar pantalla");
            Console.WriteLine("  /logs on/off  - Activar/desactivar logging a archivo");
            Console.WriteLine("  /stream on/off- Activar/desactivar modo streaming (usa: /stream on tts para TTS)");
            Console.WriteLine("  /debug on/off - Mostrar/ocultar logs detallados");
            Console.WriteLine("  /test         - Ejecutar preguntas de prueba en modelos");
            Console.WriteLine("  /models [n|id]- Listar modelos (o seleccionar: /models 1)");
            Console.WriteLine("  /model <id>   - Cambiar modelo (ej: /model 1)");
            Console.WriteLine("  /multi on/off - Activar/desactivar múltiples modelos");
            Console.WriteLine("  /add <id>     - Agregar modelo a la lista activa");
            Console.WriteLine("  /remove <id>  - Remover modelo de la lista activa");
            Console.WriteLine("  /current      - Mostrar modelos activos");
            Console.WriteLine("");
            Console.WriteLine("Ejemplos:");
            Console.WriteLine("  /models                    - Lista todos los modelos");
            Console.WriteLine("  /models 1                  - Alias de /model 1");
            Console.WriteLine("  /model 1                   - Selecciona el modelo número 1");
            Console.WriteLine("  /add 2                     - Agrega el modelo número 2 a la lista activa");
            Console.WriteLine("  /multi on                  - Activa el modo múltiples modelos");
            Console.WriteLine("  /test                      - Ejecuta 30+ preguntas de prueba");
            Console.WriteLine("  /logs on                   - Activa logging a archivo");
            Console.WriteLine("  /clear                     - Limpia la pantalla");
        }

        private static VibeVoiceClient GetTtsClient()
        {
            if (_ttsClient == null)
            {
                _ttsClient = new VibeVoiceClient(new VibeVoiceConfig
                {
                    ServerUrl = Environment.GetEnvironmentVariable("VIBEVOICE_URL") ?? "ws://localhost:3000",
                    DefaultVoice = "Carter",
                    Debug = false
                });
            }
            return _ttsClient;
        }

        private static async Task SynthesizeIfEnabledAsync(string? text)
        {
            if (!_useTts || string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\nℹ Sintetizando audio...");
                Console.ResetColor();

                var client = GetTtsClient();
                var isHealthy = await client.CheckHealthAsync();

                if (!isHealthy)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("⚠ Servidor TTS no disponible (ws://localhost:3000)");
                    Console.WriteLine("ℹ Inicia el servidor con: cd ..\\tts && start-vibevoice-server.bat");
                    Console.ResetColor();
                    return;
                }

                var outputFile = $"tts-output-{_ttsCounter++}.wav";
                var result = await client.SynthesizeAsync(text, new SynthesisOptions
                {
                    Voice = "Carter",
                    OutputFile = outputFile
                });

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Audio generado: {outputFile} ({(result.Audio.Length / 1024.0):F2} KB)");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Error TTS: {ex.Message}");
                Console.ResetColor();
            }
        }

        private static async Task RunOnce(string baseUrl, List<string> modelIds, string input, bool useStream, bool showDebug = false)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                
                if (modelIds.Count > 1)
                {
                    // Modo múltiples modelos
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[Ejecutando en {modelIds.Count} modelos: {string.Join(", ", modelIds)}]");
                    Console.ResetColor();

                    var results = await Engine.RunMultipleModelsAsync(baseUrl, modelIds, input, useStream, showDebug);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("\n=== COMPARACIÓN DE RESPUESTAS ===");
                    Console.ResetColor();

                    var firstResponse = string.Empty;
                    foreach (var (model, result) in results)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"\n--- {model} ---");
                        Console.ResetColor();

                        if (result.HasThinking)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine("[THINKING]:");
                            Console.WriteLine(result.Thinking);
                            Console.WriteLine();
                            Console.ResetColor();
                        }

                        if (result.HasResponse)
                        {
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine("[RESPONSE]:");
                            Console.WriteLine(result.Response);
                            Console.ResetColor();

                            // Guardar primera respuesta para TTS
                            if (string.IsNullOrEmpty(firstResponse))
                                firstResponse = result.Response;
                        }
                    }

                    // Sintetizar primera respuesta si TTS está activado
                    if (!string.IsNullOrEmpty(firstResponse))
                        await SynthesizeIfEnabledAsync(firstResponse);
                }
                else
                {
                    // Modo single model
                    var modelId = modelIds.First();
                    if (useStream)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"[Enviando al LLM en modo streaming...]");
                        if (showDebug)
                        {
                            Console.WriteLine($"[DEBUG INPUT]: {input}");
                            Console.WriteLine($"[DEBUG MODEL]: {modelId}");
                        }
                        Console.ResetColor();

                        using var cts = new CancellationTokenSource();
                        
                        var ticker = Task.Run(async () =>
                        {
                            var startTime = DateTime.Now;
                            while (!cts.IsCancellationRequested)
                            {
                                await Task.Delay(1000, cts.Token).ContinueWith(_ => { });
                                if (!cts.IsCancellationRequested)
                                {
                                    var elapsed = DateTime.Now - startTime;
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.Write($"\r[Tiempo: {elapsed:mm\\:ss}]");
                                    Console.ResetColor();
                                }
                            }
                        });

                        var result = await Engine.StreamAsync(baseUrl, modelId, input, cts.Token, showDebug);
                        cts.Cancel();
                        
                        try { await ticker; } catch { }

                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("\n=== RESULTADO ESTRUCTURADO ===");
                        Console.ResetColor();
                        
                        if (result.HasThinking)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.WriteLine("[PENSAMIENTO]:");
                            Console.WriteLine(result.Thinking);
                            Console.WriteLine();
                        }
                        
                        if (result.HasResponse)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("[RESPUESTA]:");
                            Console.WriteLine(result.Response);
                            Console.ResetColor();

                            // Sintetizar audio si TTS está activado
                            await SynthesizeIfEnabledAsync(result.Response);
                        }

                        if (!result.HasThinking && !result.HasResponse)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("No se recibió contenido del LLM.");
                            Console.ResetColor();
                        }
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"[Enviando al LLM...]");
                        Console.ResetColor();

                        var raw = await Engine.InvokeAsync(baseUrl, modelId, input);
                        
                        if (showDebug)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.WriteLine("[DEBUG RAW RESPONSE]:");
                            try
                            {
                                var formattedJson = JsonSerializer.Serialize(
                                    JsonDocument.Parse(raw).RootElement,
                                    new JsonSerializerOptions { WriteIndented = true }
                                );
                                Console.WriteLine(formattedJson);
                            }
                            catch
                            {
                                Console.WriteLine(raw);
                            }
                            Console.ResetColor();
                        }

                    var text = Engine.ExtractFirstText(raw);

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("=== Respuesta ===");
                        Console.ResetColor();
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[Respuesta vacía del LLM]");
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.WriteLine(text);
                            // Sintetizar audio si TTS está activado
                            await SynthesizeIfEnabledAsync(text);
                        }
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"\nDuración total: {sw.Elapsed:mm\\:ss}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error al invocar LLM: {ex.Message}");
                if (showDebug)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                Console.ResetColor();
            }
        }

        private static async Task RunTestSuiteAsync(string baseUrl, List<string> modelIds, bool useStream, bool showDebug)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== INICIANDO SUITE DE PRUEBAS ===");
            Console.WriteLine($"Modelos: {string.Join(", ", modelIds)}");
            Console.WriteLine($"Preguntas: {_testQuestions.Count}");
            Console.WriteLine($"Streaming: {(useStream ? "SI" : "NO")}");
            Console.ResetColor();
            
            Engine.LogToFile($"=== INICIO SUITE PRUEBAS ===");
            Engine.LogToFile($"Modelos: {string.Join(", ", modelIds)}");
            Engine.LogToFile($"Total preguntas: {_testQuestions.Count}");

            var totalSw = Stopwatch.StartNew();
            var results = new List<(string model, string question, ThinkingResponse response, TimeSpan duration)>();

            for (int i = 0; i < _testQuestions.Count; i++)
            {
                var question = _testQuestions[i];
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n--- Pregunta {i+1}/{_testQuestions.Count} ({question.Category}) ---");
                Console.WriteLine($"📝 {question.Question}");
                Console.ResetColor();

                Engine.LogToFile($"--- PREGUNTA {i+1}: {question.Category} ---");
                Engine.LogToFile($"Q: {question.Question}");

                var questionSw = Stopwatch.StartNew();
                
                try
                {
                    var responses = await Engine.RunMultipleModelsAsync(baseUrl, modelIds, question.Question, useStream, showDebug);
                    
                    foreach (var (model, response) in responses)
                    {
                        results.Add((model, question.Question, response, questionSw.Elapsed));
                        
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\n✅ {model} - {questionSw.Elapsed:mm\\:ss}");
                        Console.ResetColor();
                        
                        if (response.HasThinking)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine("[Thinking]: " + (response.Thinking.Length > 100 ? 
                                response.Thinking.Substring(0, 100) + "..." : response.Thinking));
                        }
                        
                        if (response.HasResponse)
                        {
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine("[Response]: " + (response.Response.Length > 150 ? 
                                response.Response.Substring(0, 150) + "..." : response.Response));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ Error en pregunta {i+1}: {ex.Message}");
                    Console.ResetColor();
                    Engine.LogToFile($"ERROR: {ex.Message}");
                }

                // Pequeña pausa entre preguntas
                await Task.Delay(1000);
            }

            totalSw.Stop();
            
            // Reporte final
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n=== SUITE DE PRUEBAS COMPLETADA ===");
            Console.WriteLine($"Duración total: {totalSw.Elapsed:mm\\:ss}");
            Console.WriteLine($"Preguntas: {_testQuestions.Count}");
            Console.WriteLine($"Modelos: {modelIds.Count}");
            Console.WriteLine($"Respuestas totales: {results.Count}");
            Console.ResetColor();

            Engine.LogToFile($"=== FIN SUITE PRUEBAS ===");
            Engine.LogToFile($"Duración total: {totalSw.Elapsed:mm\\:ss}");
            Engine.LogToFile($"Respuestas totales: {results.Count}");

            // Mostrar resumen por modelo
            foreach (var model in modelIds)
            {
                var modelResults = results.Where(r => r.model == model).ToList();
                var avgTime = modelResults.Any() ? 
                    TimeSpan.FromMilliseconds(modelResults.Average(r => r.duration.TotalMilliseconds)) : 
                    TimeSpan.Zero;
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n📊 {model}:");
                Console.WriteLine($"   Respuestas: {modelResults.Count}/{_testQuestions.Count}");
                Console.WriteLine($"   Tiempo promedio: {avgTime:mm\\:ss\\.ff}");
                Console.ResetColor();
            }
        }

    }
}

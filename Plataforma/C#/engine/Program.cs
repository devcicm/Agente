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

        public static async Task<List<ModelInfo>> GetModelsAsync(string baseUrl)
        {
            var models = new List<ModelInfo>();
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("=== Obteniendo modelos disponibles ===");
                Console.ResetColor();

                var response = await Http.GetAsync($"{baseUrl.TrimEnd('/')}/v1/models");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(json);
                            
                            // Intentar diferentes estructuras de respuesta
                            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                            {
                                // Estructura: { "data": [ { "id": "...", ... } ] }
                                foreach (var modelElement in data.EnumerateArray())
                                {
                                    var model = new ModelInfo();
                                    
                                    if (modelElement.TryGetProperty("id", out var id))
                                        model.Id = id.GetString() ?? string.Empty;
                                    
                                    if (modelElement.TryGetProperty("name", out var name))
                                        model.Name = name.GetString() ?? string.Empty;
                                    else
                                        model.Name = model.Id; // Usar ID como nombre si no hay name
                                    
                                    if (modelElement.TryGetProperty("description", out var description))
                                        model.Description = description.GetString() ?? string.Empty;

                                    if (!string.IsNullOrEmpty(model.Id))
                                        models.Add(model);
                                }
                            }
                            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                // Estructura: [ { "id": "...", ... } ]
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
                            else
                            {
                                // Estructura diferente, mostrar debug
                                Console.ForegroundColor = ConsoleColor.DarkYellow;
                                Console.WriteLine("Estructura de respuesta no reconocida:");
                                Console.WriteLine(json);
                                Console.ResetColor();
                            }
                        }
                        catch (JsonException ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Error parseando respuesta de modelos: {ex.Message}");
                            Console.ResetColor();
                        }
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error obteniendo modelos: {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(errorContent))
                    {
                        Console.WriteLine($"Detalles: {errorContent}");
                    }
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error al obtener modelos: {ex.Message}");
                Console.ResetColor();
            }

            return models;
        }

        public static async Task<string> InvokeAsync(string baseUrl, string modelId, string input)
        {
            var payload = new
            {
                model = modelId,
                input,
                stream = false
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/v1/responses")
            {
                Content = content
            };

            using var response = await Http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
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
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] HTTP {response.StatusCode}: {errorContent}");
                Console.ResetColor();
                return result;
            }
            
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            // Variables para el procesamiento
            var fullContent = new StringBuilder();
            var rawEvents = new List<string>();

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync();
                
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Guardar el evento raw completo
                rawEvents.Add(line);

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
                                // Procesar caracteres especiales y agregar al contenido
                                var processedChunk = ProcessSpecialCharacters(chunk);
                                fullContent.Append(processedChunk);
                                
                                // Mostrar en tiempo real
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
                        // Mostrar el JSON completo formateado
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

            // Procesar el contenido final
            var finalContent = fullContent.ToString();
            if (showDebug)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"\n[DEBUG CONTENIDO COMPLETO ACUMULADO]:");
                Console.WriteLine(EscapeControlCharacters(finalContent));
                Console.ResetColor();
            }

            // Procesar separación thinking/response
            ProcessFinalContent(finalContent, result);
            
            Console.WriteLine();
            return result;
        }

        private static string EscapeControlCharacters(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Reemplazar caracteres de control con representaciones legibles
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

            // Manejar secuencias comunes de escape
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

            // Manejar otros escapes comunes
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
                return json; // Devolver original si no se puede formatear
            }
        }

        private static void ProcessFinalContent(string fullContent, ThinkingResponse result)
        {
            // Algoritmo simplificado y robusto para separar thinking y response
            var (thinking, response) = SimpleContentSeparation(fullContent);
            
            result.Thinking = thinking.Trim();
            result.Response = response.Trim();
        }

        private static (string thinking, string response) SimpleContentSeparation(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return (string.Empty, string.Empty);

            // Estrategia 1: Buscar transiciones claras basadas en patrones comunes
            var transitionPatterns = new[]
            {
                @"¡Hola\s*[!]?", @"Hola[!]?\s+", @"Hola,\s+",
                @"\*\*", @"###", @"---", @"\.\s*$",
                @"[.!?]\s+[A-ZÁÉÍÓÚÜ]",
                @"\b(La|El|Los|Las|Un|Una)\s+[A-Z]",
                @"\b(Marco|Aurelio|Fórmula|Agua|H₂?O|H2O)\b"
            };

            // Buscar el mejor punto de transición
            int bestTransition = -1;
            
            foreach (var pattern in transitionPatterns)
            {
                try
                {
                    var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        var position = match.Index;
                        
                        // Validar que sea una transición válida
                        if (IsValidTransitionPoint(content, position))
                        {
                            if (bestTransition == -1 || position < bestTransition)
                            {
                                bestTransition = position;
                            }
                        }
                    }
                }
                catch (ArgumentException ex)
                {
                    // Ignorar patrones inválidos
                    continue;
                }
            }

            // Estrategia 2: Buscar cambio de inglés a español
            if (bestTransition == -1)
            {
                bestTransition = FindLanguageTransition(content);
            }

            // Estrategia 3: Buscar el último punto antes de una respuesta en español
            if (bestTransition == -1)
            {
                bestTransition = FindLastEnglishSegment(content);
            }

            if (bestTransition > 0 && bestTransition < content.Length)
            {
                var thinkingPart = content.Substring(0, bestTransition).Trim();
                var responsePart = content.Substring(bestTransition).Trim();

                // Validar que la separación tenga sentido
                if (thinkingPart.Length > 10 && responsePart.Length > 5 && 
                    IsMostlyEnglish(thinkingPart) && !IsMostlyEnglish(responsePart))
                {
                    return (thinkingPart, responsePart);
                }
            }

            // Fallback: Si no podemos separar, analizar el contenido completo
            return AnalyzeCompleteContent(content);
        }

        private static bool IsValidTransitionPoint(string content, int position)
        {
            if (position <= 0 || position >= content.Length) return false;

            // Verificar que antes del punto haya principalmente inglés
            var before = content.Substring(0, position);
            var after = content.Substring(position);

            return IsMostlyEnglish(before) && !IsMostlyEnglish(after) && after.Length > 5;
        }

        private static int FindLanguageTransition(string content)
        {
            // Buscar transición de inglés a español usando ventanas
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
            // Buscar el último segmento que sea principalmente inglés
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
                // Reconstruir el contenido hasta el último segmento inglés
                var thinkingPart = string.Join(" ", sentences.Take(englishCount));
                return thinkingPart.Length;
            }

            return -1;
        }

        private static (string thinking, string response) AnalyzeCompleteContent(string content)
        {
            // Si todo es inglés, es thinking
            if (IsMostlyEnglish(content))
                return (content, string.Empty);

            // Si hay muy poco inglés al principio, es response
            var firstSentence = GetFirstSentence(content);
            if (!IsMostlyEnglish(firstSentence) || content.Length - firstSentence.Length < 10)
                return (string.Empty, content);

            // Por defecto, considerar todo como thinking
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
            if (words.Count == 0) return true; // Por defecto, considerar inglés si no hay palabras

            var englishCount = words.Cast<Match>()
                .Count(match => englishWords.Contains(match.Value));

            var spanishCount = words.Cast<Match>()
                .Count(match => spanishWords.Contains(match.Value));

            // Si hay más palabras en inglés que en español, es inglés
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
                    
                    // Mostrar el JSON de respuesta formateado
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
    }

    internal class Program
    {
        private const string DefaultModel = "gpt-oss-20b-gpt-5-reasoning-distill";
        private const string DefaultBaseUrl = "http://localhost:1234";

        public static async Task Main(string[] args)
        {
            var baseUrl = Environment.GetEnvironmentVariable("LMSTUDIO_URL") ?? DefaultBaseUrl;
            var modelId = Environment.GetEnvironmentVariable("LMSTUDIO_MODEL") ?? DefaultModel;
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

            await Engine.TestLmStudioApi(baseUrl, modelId);
            Console.WriteLine();

            if (args.Length > 0 && !args[0].StartsWith("--"))
            {
                var inputOnce = string.Join(" ", args.Where(a => !a.StartsWith("--")));
                await RunOnce(baseUrl, modelId, inputOnce, useStream, showDebug);
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==== ENGINE LLM - CLI (COMANDOS CORREGIDOS) ====");
            Console.ResetColor();
            Console.WriteLine($"Modelo actual:   {modelId}");
            Console.WriteLine($"Endpoint:        {baseUrl}");
            Console.WriteLine("Comandos:");
            Console.WriteLine("  /exit       - Salir del programa");
            Console.WriteLine("  /help       - Mostrar ayuda");
            Console.WriteLine("  /stream on  - Activar modo streaming");
            Console.WriteLine("  /stream off - Desactivar modo streaming");
            Console.WriteLine("  /debug on   - Mostrar logs detallados y JSON raw");
            Console.WriteLine("  /debug off  - Ocultar logs detallados");
            Console.WriteLine("  /test       - Probar conexión con LM Studio");
            Console.WriteLine("  /models     - Listar modelos disponibles");
            Console.WriteLine("  /model <id> - Cambiar modelo (ej: /model gpt-4)");
            Console.WriteLine("  /current    - Mostrar modelo actual");
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
                
                // Verificar si es un comando
                var isCommand = ProcessCommand(line, ref baseUrl, ref modelId, ref useStream, ref showDebug);
                
                if (isCommand)
                {
                    Console.WriteLine();
                    continue;
                }

                // Si no es un comando, enviar al LLM
                await RunOnce(baseUrl, modelId, line, useStream, showDebug);
                Console.WriteLine();
            }
        }

        private static bool ProcessCommand(string line, ref string baseUrl, ref string modelId, ref bool useStream, ref bool showDebug)
        {
            // Convertir a minúsculas para comparación case-insensitive
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
            
            if (lowerLine.StartsWith("/stream"))
            {
                useStream = line.ToLowerInvariant().Contains("on");
                Console.WriteLine($"Streaming {(useStream ? "activado" : "desactivado")}.");
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
                _ = Engine.TestLmStudioApi(baseUrl, modelId); // Fire and forget
                return true;
            }
            
            if (lowerLine == "/models")
            {
                _ = ListModelsAsync(baseUrl); // Fire and forget
                return true;
            }
            
            if (lowerLine.StartsWith("/model "))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var newModel = parts[1];
                    return ChangeModel(baseUrl, newModel, ref modelId);
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
            
            if (lowerLine == "/current")
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Modelo actual: {modelId}");
                Console.ResetColor();
                return true;
            }

            // Si no es un comando reconocido
            if (line.StartsWith("/"))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Comando no reconocido: {line}");
                Console.WriteLine("Escribe /help para ver los comandos disponibles.");
                Console.ResetColor();
                return true;
            }

            // No es un comando
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

        private static bool ChangeModel(string baseUrl, string newModel, ref string modelId)
        {
            // Si es un número, buscar en la lista de modelos
            if (int.TryParse(newModel, out int modelNumber))
            {
                // Necesitamos obtener los modelos de forma síncrona para esta operación
                var modelsTask = Engine.GetModelsAsync(baseUrl);
                modelsTask.Wait(); // Esto no es ideal, pero funciona para un CLI
                var models = modelsTask.Result;
                
                if (modelNumber > 0 && modelNumber <= models.Count)
                {
                    modelId = models[modelNumber - 1].Id;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Modelo cambiado a: {modelId}");
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
                // Asumir que es un ID de modelo
                modelId = newModel;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Modelo cambiado a: {modelId}");
                Console.ResetColor();
                return true;
            }
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Comandos disponibles:");
            Console.WriteLine("  /exit       - Salir del programa");
            Console.WriteLine("  /help       - Mostrar esta ayuda");
            Console.WriteLine("  /stream on  - Activar modo streaming");
            Console.WriteLine("  /stream off - Desactivar modo streaming");
            Console.WriteLine("  /debug on   - Mostrar logs detallados y JSON raw");
            Console.WriteLine("  /debug off  - Ocultar logs detallados");
            Console.WriteLine("  /test       - Probar conexión con LM Studio");
            Console.WriteLine("  /models     - Listar modelos disponibles");
            Console.WriteLine("  /model <id> - Cambiar modelo (ej: /model gpt-4)");
            Console.WriteLine("  /current    - Mostrar modelo actual");
            Console.WriteLine("");
            Console.WriteLine("Ejemplos:");
            Console.WriteLine("  /models                    - Lista todos los modelos");
            Console.WriteLine("  /model 1                   - Selecciona el modelo número 1");
            Console.WriteLine("  /model gpt-4               - Selecciona el modelo con ID 'gpt-4'");
            Console.WriteLine("  /stream on                 - Activa el modo streaming");
            Console.WriteLine("  Hola, ¿cómo estás?         - Envía un mensaje al LLM");
        }

        private static async Task RunOnce(string baseUrl, string modelId, string input, bool useStream, bool showDebug = false)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                
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

                    // Mostrar resultados estructurados
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
                    }

                    if (!result.HasThinking && !result.HasResponse)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("No se recibió contenido del LLM.");
                        Console.ResetColor();
                    }

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"\nDuración total: {sw.Elapsed:mm\\:ss}");
                    Console.ResetColor();
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

                    var text = ExtractFirstText(raw);

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
                    }
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"(Duración: {sw.Elapsed:mm\\:ss})");
                    Console.ResetColor();
                }
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

        private static string ExtractFirstText(string raw)
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
    }
}
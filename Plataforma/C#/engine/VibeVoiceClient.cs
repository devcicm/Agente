using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EngineConsole
{
    /// <summary>
    /// Cliente para Microsoft VibeVoice TTS
    /// Conecta vía WebSocket para síntesis de voz en tiempo real
    /// </summary>
    public class VibeVoiceClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _serverUrl;
        private readonly string _defaultVoice;
        private readonly double _cfgScale;
        private readonly int _steps;
        private readonly int _timeout;
        private readonly bool _debug;
        private List<string> _availableVoices = new();

        public VibeVoiceClient(VibeVoiceConfig? config = null)
        {
            config ??= new VibeVoiceConfig();

            _serverUrl = config.ServerUrl ?? "ws://localhost:3000";
            _defaultVoice = config.DefaultVoice ?? "Carter";
            _cfgScale = config.CfgScale ?? 1.5;
            _steps = config.Steps ?? 5;
            _timeout = config.Timeout ?? 120000;
            _debug = config.Debug ?? false;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(_timeout)
            };
        }

        /// <summary>
        /// Verifica si el servidor está disponible
        /// </summary>
        public async Task<bool> CheckHealthAsync()
        {
            try
            {
                var httpUrl = _serverUrl.Replace("ws://", "http://").Replace("wss://", "https://");
                var response = await _httpClient.GetAsync($"{httpUrl}/config");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Obtiene las voces disponibles del servidor
        /// </summary>
        public async Task<List<string>> ListVoicesAsync()
        {
            try
            {
                var httpUrl = _serverUrl.Replace("ws://", "http://").Replace("wss://", "https://");
                var response = await _httpClient.GetAsync($"{httpUrl}/config");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("voices", out var voicesArray))
                    {
                        _availableVoices = new List<string>();
                        foreach (var voice in voicesArray.EnumerateArray())
                        {
                            _availableVoices.Add(voice.GetString() ?? "");
                        }
                    }
                }

                return _availableVoices;
            }
            catch (Exception ex)
            {
                if (_debug)
                {
                    Console.WriteLine($"[VibeVoice] Error obteniendo voces: {ex.Message}");
                }
                return new List<string>();
            }
        }

        /// <summary>
        /// Sintetiza texto a audio (modo buffered)
        /// </summary>
        public async Task<SynthesisResult> SynthesizeAsync(
            string text,
            SynthesisOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new SynthesisOptions();

            var voice = options.Voice ?? _defaultVoice;
            var cfgScale = options.CfgScale ?? _cfgScale;
            var steps = options.Steps ?? _steps;

            var audioChunks = new List<byte[]>();
            var logs = new List<JsonElement>();
            var startTime = DateTime.Now;

            using var ws = new ClientWebSocket();

            var wsUrl = $"{_serverUrl}/stream?text={Uri.EscapeDataString(text)}&voice={Uri.EscapeDataString(voice)}&cfg={cfgScale}&steps={steps}";

            if (_debug)
            {
                Console.WriteLine($"[VibeVoice] Conectando a: {wsUrl}");
            }

            try
            {
                await ws.ConnectAsync(new Uri(wsUrl), cancellationToken);

                if (_debug)
                {
                    Console.WriteLine("[VibeVoice] WebSocket conectado");
                }

                var buffer = new byte[8192];

                while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        // Log message
                        var logText = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        try
                        {
                            var log = JsonDocument.Parse(logText);
                            logs.Add(log.RootElement.Clone());

                            if (_debug && log.RootElement.TryGetProperty("event", out var eventProp))
                            {
                                Console.WriteLine($"[VibeVoice] {eventProp.GetString()}");
                            }
                        }
                        catch { }
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        // Audio chunk
                        var chunk = new byte[result.Count];
                        Array.Copy(buffer, 0, chunk, 0, result.Count);
                        audioChunks.Add(chunk);

                        if (_debug && audioChunks.Count % 10 == 0)
                        {
                            Console.WriteLine($"[VibeVoice] Recibidos {audioChunks.Count} chunks");
                        }
                    }
                }

                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (_debug)
                {
                    Console.WriteLine($"[VibeVoice] Error: {ex.Message}");
                }
                throw new Exception($"VibeVoice synthesis failed: {ex.Message}", ex);
            }

            var duration = DateTime.Now - startTime;

            // Concatenar chunks
            var totalSize = 0;
            foreach (var chunk in audioChunks)
            {
                totalSize += chunk.Length;
            }

            var audioBuffer = new byte[totalSize];
            var offset = 0;
            foreach (var chunk in audioChunks)
            {
                Array.Copy(chunk, 0, audioBuffer, offset, chunk.Length);
                offset += chunk.Length;
            }

            if (_debug)
            {
                Console.WriteLine($"[VibeVoice] Síntesis completa:");
                Console.WriteLine($"  - Duración: {duration.TotalMilliseconds}ms");
                Console.WriteLine($"  - Chunks: {audioChunks.Count}");
                Console.WriteLine($"  - Tamaño: {audioBuffer.Length} bytes");
            }

            // Guardar archivo si se especificó
            if (!string.IsNullOrEmpty(options.OutputFile))
            {
                try
                {
                    var wavBuffer = PcmToWav(audioBuffer);
                    await File.WriteAllBytesAsync(options.OutputFile, wavBuffer, cancellationToken);

                    if (_debug)
                    {
                        Console.WriteLine($"[VibeVoice] Guardado en: {options.OutputFile}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VibeVoice] Error guardando archivo: {ex.Message}");
                }
            }

            return new SynthesisResult
            {
                Audio = audioBuffer,
                Duration = (int)duration.TotalMilliseconds,
                Chunks = audioChunks.Count,
                SampleRate = 24000,
                Format = "PCM16"
            };
        }

        /// <summary>
        /// Convierte PCM16 a formato WAV con headers
        /// </summary>
        public static byte[] PcmToWav(byte[] pcmData, int sampleRate = 24000, short numChannels = 1, short bitsPerSample = 16)
        {
            var byteRate = sampleRate * numChannels * bitsPerSample / 8;
            var blockAlign = (short)(numChannels * bitsPerSample / 8);
            var dataSize = pcmData.Length;
            var headerSize = 44;

            var wavBuffer = new byte[headerSize + dataSize];

            using var stream = new MemoryStream(wavBuffer);
            using var writer = new BinaryWriter(stream);

            // RIFF header
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // fmt chunk size
            writer.Write((short)1); // PCM format
            writer.Write(numChannels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);

            // data chunk
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            writer.Write(pcmData);

            return wavBuffer;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Configuración del cliente VibeVoice
    /// </summary>
    public class VibeVoiceConfig
    {
        public string? ServerUrl { get; set; } = "ws://localhost:3000";
        public string? DefaultVoice { get; set; } = "Carter";
        public double? CfgScale { get; set; } = 1.5;
        public int? Steps { get; set; } = 5;
        public int? Timeout { get; set; } = 120000;
        public bool? Debug { get; set; } = false;
    }

    /// <summary>
    /// Opciones para síntesis de voz
    /// </summary>
    public class SynthesisOptions
    {
        public string? Voice { get; set; }
        public double? CfgScale { get; set; }
        public int? Steps { get; set; }
        public string? OutputFile { get; set; }
    }

    /// <summary>
    /// Resultado de síntesis
    /// </summary>
    public class SynthesisResult
    {
        public byte[] Audio { get; set; } = Array.Empty<byte>();
        public int Duration { get; set; }
        public int Chunks { get; set; }
        public int SampleRate { get; set; }
        public string Format { get; set; } = "";
    }
}

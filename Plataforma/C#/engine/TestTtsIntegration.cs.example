using System;
using System.Threading.Tasks;
using EngineConsole;

namespace EngineConsole
{
    public class TestTtsIntegration
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.WriteLine("  Test de Integración TTS - Engine C#");
            Console.WriteLine("═══════════════════════════════════════════════════\n");

            try
            {
                // Crear cliente
                var client = new VibeVoiceClient(new VibeVoiceConfig
                {
                    ServerUrl = "ws://localhost:3000",
                    DefaultVoice = "en-Carter_man",
                    Debug = true
                });

                Console.WriteLine("1. Verificando servidor VibeVoice...");
                var isHealthy = await client.CheckHealthAsync();

                if (!isHealthy)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Servidor TTS no disponible en ws://localhost:3000");
                    Console.ResetColor();
                    Console.WriteLine("\nInicia el servidor con:");
                    Console.WriteLine("  cd ..\\..\\tts");
                    Console.WriteLine("  start-vibevoice-server.bat\n");
                    return;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Servidor disponible\n");
                Console.ResetColor();

                // Listar voces
                Console.WriteLine("2. Obteniendo voces disponibles...");
                var voices = await client.ListVoicesAsync();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ {voices.Count} voces disponibles");
                Console.ResetColor();
                Console.WriteLine($"   Voces: {string.Join(", ", voices.GetRange(0, Math.Min(5, voices.Count)))}...\n");

                // Síntesis de prueba
                Console.WriteLine("3. Sintetizando texto de prueba...");
                var testText = "Hello! This is a test of the C# text-to-speech integration.";
                Console.WriteLine($"   Texto: \"{testText}\"");
                Console.WriteLine($"   Voz: en-Carter_man\n");

                var startTime = DateTime.Now;
                var result = await client.SynthesizeAsync(testText, new SynthesisOptions
                {
                    Voice = "en-Carter_man",
                    OutputFile = "test-tts-integration-csharp.wav"
                });

                var duration = DateTime.Now - startTime;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Síntesis completada:");
                Console.ResetColor();
                Console.WriteLine($"   - Duración total: {duration.TotalMilliseconds}ms");
                Console.WriteLine($"   - Chunks recibidos: {result.Chunks}");
                Console.WriteLine($"   - Tamaño audio: {(result.Audio.Length / 1024.0):F2} KB");
                Console.WriteLine($"   - Sample rate: {result.SampleRate} Hz");
                Console.WriteLine($"   - Formato: {result.Format}");
                Console.WriteLine($"   - Archivo: test-tts-integration-csharp.wav\n");

                Console.WriteLine("═══════════════════════════════════════════════════");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ TEST EXITOSO - TTS funcionando correctamente");
                Console.ResetColor();
                Console.WriteLine("═══════════════════════════════════════════════════\n");

                Console.WriteLine("El archivo test-tts-integration-csharp.wav fue generado.");
                Console.WriteLine("Puedes reproducirlo con cualquier reproductor de audio.\n");

                client.Dispose();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
        }
    }
}

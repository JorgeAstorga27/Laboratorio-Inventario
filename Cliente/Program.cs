using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Configuración

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("config.example.json", optional: true)
    .AddJsonFile("config.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var mcpUrl      = config["mcp:url"];
var llmBase     = config["llm:baseUrl"]?.TrimEnd('/');
var chatPath    = config["llm:chatPath"] ?? "/api/chat";
var modelo      = config["llm:model"];
var maxRondas   = int.TryParse(config["chat:maxLlamadasPorPregunta"], out var n) && n > 0 ? n : 8;
var temperatura = double.TryParse(config["llm:temperature"], System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0;
var contexto    = int.TryParse(config["llm:numCtx"], out var c) && c > 0 ? c : 8192;
var timeout     = int.TryParse(config["llm:timeoutSegundos"], out var s) && s > 0 ? s : 600;

if (string.IsNullOrWhiteSpace(mcpUrl) || string.IsNullOrWhiteSpace(llmBase) || string.IsNullOrWhiteSpace(modelo)){
    Error("Falta configuración: revisa mcp:url, llm:baseUrl y llm:model en config.json.");
    return 1;
}

var llmUrl      = llmBase + chatPath;
var jsonLegible = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

Console.WriteLine("Configuración efectiva");
Console.WriteLine($"  Modelo local: {modelo}");
Console.WriteLine($"  API del LLM:  {llmUrl}");
Console.WriteLine($"  Servidor MCP: {mcpUrl}");
Console.WriteLine();

// Conexión al servidor MCP

Console.WriteLine("Conectando al servidor MCP...");

McpClient mcp;
try{
    mcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(mcpUrl)
    }));
}
catch (Exception ex){
    Error($"No se pudo conectar a {mcpUrl}. Error: ({ex.Message})");
    return 1;
}
await using var _ = mcp;

var herramientas = await mcp.ListToolsAsync();

Console.WriteLine("Herramientas MCP descubiertas:");
foreach (var h in herramientas)
    Console.WriteLine($"  - {h.Name}: {h.Description}");

// Definiciones en el formato que espera Ollama
var toolsLlm = new JsonArray(herramientas.Select(h => (JsonNode)new JsonObject
{
    ["type"] = "function",
    ["function"] = new JsonObject
    {
        ["name"]        = h.Name,
        ["description"] = h.Description,
        ["parameters"]  = JsonNode.Parse(h.ProtocolTool.InputSchema.GetRawText())
    }
}).ToArray());

// Instrucciones del sistema

var instrucciones = $"""
    Eres el asistente del inventario de un laboratorio. Quien te escribe es el gestor del inventario.
    Responde siempre en español y de forma breve. La fecha de hoy es {DateTime.Today:yyyy-MM-dd}.

    Usa las herramientas disponibles para resolver lo que pida el usuario. Lee sus descripciones para saber
    cuál usar y combínalas cuando una necesite datos que da otra.

    - El usuario no conoce los ids internos; nunca se los pidas. Obtenlos tú con las herramientas a partir
      de los nombres que él mencione.
    - Actúa sin pedir confirmación. Pregunta solo si hay ambigüedad o no encuentras lo que menciona.
    - No anuncies lo que vas a hacer: hazlo. Responde con texto solo cuando hayas completado todo lo que
      pidió el usuario (o no puedas continuar).
    - No inventes datos: todo lo que afirmes debe venir de una herramienta.
    - Si una herramienta falla, lee el error y corrige o explícalo. Solo confirma una acción si tuvo éxito.
    - Recuerda que para hacer una operacion primero tienes que verificar los datos, NO DEBES OLVIDAR ESTA REGLA.
    """;

// Chat con el LLM

var mensajes = new List<JsonNode>();
NuevaConversacion();

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeout) };

Console.WriteLine();
Console.WriteLine("Escribe tu pregunta. Comandos: /nuevo (nueva conversación), /salir.");

while (true){
    Console.Write("\nUsuario: ");
    var entrada = Console.ReadLine()?.Trim();

    if (entrada is null || entrada.Equals("/salir", StringComparison.OrdinalIgnoreCase))
        break;

    if (entrada.Length == 0)
        continue;

    if (entrada.Equals("/nuevo", StringComparison.OrdinalIgnoreCase)){
        NuevaConversacion();
        Console.WriteLine("Conversación nueva iniciada.");
        continue;
    }

    var inicio = mensajes.Count;
    mensajes.Add(new JsonObject { ["role"] = "user", ["content"] = entrada });

    try{
        await Responder();
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException){
        mensajes.RemoveRange(inicio, mensajes.Count - inicio);
        Error($"No se pudo comunicar con el LLM en {llmUrl}: {ex.Message}");
    }
    catch (Exception ex){
        // Respuesta inesperada del LLM
        mensajes.RemoveRange(inicio, mensajes.Count - inicio);
        Error($"El LLM devolvió una respuesta que no se pudo procesar: {ex.Message}");
    }
}

return 0;

// Funciones

void NuevaConversacion(){
    mensajes.Clear();
    mensajes.Add(new JsonObject { ["role"] = "system", ["content"] = instrucciones });
}

async Task Responder(){
    for (var ronda = 1; ronda <= maxRondas; ronda++){
        Console.WriteLine($"\n[LLM] Enviando historial y {toolsLlm.Count} herramientas a {modelo} (llamada {ronda})...");

        var mensaje = (await PedirLLM())["message"]!.AsObject();
        mensajes.Add(mensaje.DeepClone());

        // Modelo decidió dar la respuesta final
        if (mensaje["tool_calls"] is not JsonArray llamadas || llamadas.Count == 0){
            Console.WriteLine($"\nAsistente: {mensaje["content"]?.GetValue<string>()}");
            return;
        }

        foreach (var llamada in llamadas){
            var nombre     = llamada!["function"]!["name"]!.GetValue<string>();
            var argumentos = llamada["function"]!["arguments"] as JsonObject ?? new JsonObject();

            Console.WriteLine($"[MCP] El modelo solicita: {nombre}");
            Console.WriteLine($"[MCP] Argumentos: {argumentos.ToJsonString(jsonLegible)}");
            Console.WriteLine("[MCP] Llamando al servidor MCP...");

            var resultado = await LlamarHerramienta(nombre, argumentos);
            Console.WriteLine($"[MCP] Resultado: {resultado}");

            mensajes.Add(new JsonObject
            {
                ["role"]      = "tool",
                ["tool_name"] = nombre,
                ["content"]   = resultado
            });
        }
    }

    Console.WriteLine($"\nAsistente: no pude terminar la respuesta tras {maxRondas} llamadas al modelo. Intenta reformular la pregunta.");
}

async Task<JsonNode> PedirLLM(){
    var cuerpo = new JsonObject
    {
        ["model"]    = modelo,
        ["stream"]   = false,
        ["messages"] = new JsonArray(mensajes.Select(m => m.DeepClone()).ToArray()),
        ["tools"]    = toolsLlm.DeepClone(),
        ["options"]  = new JsonObject { ["temperature"] = temperatura, ["num_ctx"] = contexto }
    };

    using var respuesta = await http.PostAsJsonAsync(llmUrl, cuerpo);
    var texto = await respuesta.Content.ReadAsStringAsync();

    if (!respuesta.IsSuccessStatusCode)
        throw new HttpRequestException($"{(int)respuesta.StatusCode} {texto}");

    return JsonNode.Parse(texto)!;
}

async Task<string> LlamarHerramienta(string nombre, JsonObject argumentos){
    try{
        var valores = argumentos.ToDictionary(
            a => a.Key,
            a => (object?)JsonSerializer.SerializeToElement(a.Value));

        var resultado = await mcp.CallToolAsync(nombre, valores);
        var texto     = string.Join("\n", resultado.Content.OfType<TextContentBlock>().Select(c => c.Text));

        return texto.Length > 0 ? texto : "Sin resultado.";
    }
    catch (Exception ex){
        return JsonSerializer.Serialize(new { exito = false, error = $"Fallo al llamar {nombre}: {ex.Message}" }, jsonLegible);
    }
}

static void Error(string mensaje){
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(mensaje);
    Console.ResetColor();
}
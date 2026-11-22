using Laboratorio.Datos;
using Laboratorio.Servicios;

var builder = WebApplication.CreateBuilder(args);

//Dirección del servidor desde configuración
var host   = builder.Configuration["Servidor:Host"] ?? "localhost";
var puerto = builder.Configuration.GetValue("Servidor:Puerto", 5000);
var ruta   = builder.Configuration["Servidor:Ruta"] ?? "/mcp";

builder.WebHost.UseUrls($"http://{host}:{puerto}");

//Registrar repositorio y servicio para inyección de dependencias
builder.Services.AddSingleton<RepositorioLaboratorio>();
builder.Services.AddSingleton<ServicioLaboratorio>();

//Registrar el servidor MCP con transporte HTTP
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

//Mapear la ruta MCP
app.MapMcp(ruta);

//Comprobar en el navegador que el servidor está arriba
app.MapGet("/", () => $"Servidor MCP de Laboratorio activo. Endpoint MCP: {ruta}");

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine(" Servidor MCP de Laboratorio iniciado");
    Console.WriteLine($" URL MCP: http://{host}:{puerto}{ruta}");
});

app.Run();

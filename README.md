# Inventario de Laboratorio — Asistente con MCP y LLM local

Asistente de consola que administra los **préstamos de instrumentos de un laboratorio**. Un modelo de lenguaje local (Ollama) decide cuándo y con qué parámetros invocar las herramientas que expone un servidor **Model Context Protocol (MCP)** propio, escrito en C#/.NET.

```
usuario -> cliente/chat -> LLM local (Ollama) -> llamada MCP -> herramienta (SQLite) -> LLM local -> respuesta al usuario
```

## Contenido

- [Estructura del proyecto](#estructura-del-proyecto)
- [Herramientas MCP](#herramientas-mcp)
- [Requisitos](#requisitos)
- [Instalación](#instalación)
- [Configuración](#configuración)
- [Ejecución](#ejecución)
- [Datos de prueba](#datos-de-prueba)
- [Evidencias](#evidencias)
- [Solución de problemas](#solución-de-problemas)

## Estructura del proyecto

```
Inventario-Laboratorio/
├── Servidor/                         Servidor MCP (ASP.NET Core, transporte HTTP)
│   ├── Program.cs                    Arranque, host/puerto/ruta desde configuración
│   ├── appsettings.json              Dirección del servidor y reglas del laboratorio
│   └── src/
│       ├── Herramientas/HerramientasLaboratorio.cs   Definición de las herramientas MCP
│       ├── Servicios/ServicioLaboratorio.cs          Validaciones y reglas de negocio
│       └── Datos/                                    Modelos y repositorio SQLite
└── Cliente/                          Chat de consola
    ├── Program.cs                    Conexión MCP, llamadas a Ollama y ciclo de herramientas
    ├── config.example.json           Configuración de ejemplo (sin secretos)
    └── config.json                   Configuración efectiva
```

## Herramientas MCP

Todas las herramientas devuelven un objeto JSON con la misma forma: `{ "exito": true, "datos": ... }` si funcionan, o `{ "exito": false, "error": "mensaje comprensible" }` si algo falla.

| Herramienta | Parámetros | Qué hace | Validaciones |
|---|---|---|---|
| `BuscarTrabajadores` | `texto` (string, opcional) | Busca trabajadores por nombre o apellido, sin importar mayúsculas ni acentos. Sin texto, lista a todos. | Máximo 100 caracteres; avisa si no hay coincidencias. |
| `BuscarInstrumentos` | `texto` (string, opcional) | Busca instrumentos y devuelve unidades totales y **disponibles**. Sin texto, lista todo el inventario. | Máximo 100 caracteres; avisa si no hay coincidencias. |
| `RegistrarPrestamo` | `idTrabajador` (int), `idInstrumento` (int) | Registra el préstamo de un instrumento con la fecha de hoy. | Ids mayores que 0; el trabajador y el instrumento deben existir; debe haber unidades disponibles; el trabajador no puede superar el máximo de préstamos activos (3 por defecto). |
| `ConsultarPrestamosActivos` | `idTrabajador` (int, opcional) | Lista los préstamos sin devolver, con días prestado. | Id mayor que 0 y trabajador existente. |
| `CompletarPrestamo` | `idPrestamo` (int) | Registra la devolución de un préstamo. | Id mayor que 0; el préstamo debe existir y no estar ya devuelto. |

Los datos se guardan en SQLite (`Servidor/laboratorio.db`). La base se crea y se llena con datos de prueba automáticamente la primera vez que arranca el servidor.

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com/download)
- Un modelo con soporte de herramientas (*tool calling*). El proyecto se probó con `qwen2.5:7b`.

## Instalación

```bash
# 1. Clonar el repositorio
git clone <url-del-repositorio>
cd Inventario-Laboratorio

# 2. Restaurar dependencias de NuGet
dotnet restore Servidor/Servidor.csproj
dotnet restore Cliente/Cliente.csproj

# 3. Descargar el modelo local
ollama pull qwen2.5:7b
```

Paquetes NuGet que se instalan:

| Proyecto | Paquetes |
|---|---|
| Servidor | `ModelContextProtocol`, `ModelContextProtocol.AspNetCore`, `Microsoft.Data.Sqlite` |
| Cliente | `ModelContextProtocol`, `Microsoft.Extensions.Configuration.Json`, `.EnvironmentVariables`, `.CommandLine` |

## Configuración

Ninguna dirección ni nombre de modelo está fija en el código. Todos se pueden cambiar con el archivo de configuración, con variables de entorno o con argumentos de línea de comandos. Si se usan varias a la vez, el orden de prioridad es: **argumentos > variables de entorno > `config.json` > `config.example.json`**.

### Servidor — `Servidor/appsettings.json`

```json
{
  "Servidor": {
    "Host": "localhost",
    "Puerto": 5000,
    "Ruta": "/mcp"
  },
  "Laboratorio": {
    "MaxPrestamos": 3
  }
}
```

| Clave | Descripción | Valor por defecto |
|---|---|---|
| `Servidor:Host` | Interfaz de red en la que escucha. Usa `0.0.0.0` para aceptar conexiones de otras máquinas. | `localhost` |
| `Servidor:Puerto` | Puerto HTTP. | `5000` |
| `Servidor:Ruta` | Ruta del endpoint MCP. | `/mcp` |
| `Laboratorio:MaxPrestamos` | Máximo de préstamos activos por trabajador. | `3` |

### Cliente — `Cliente/config.json`

Copia el archivo de ejemplo y ajústalo si hace falta:

```bash
cp Cliente/config.example.json Cliente/config.json
```

```json
{
  "mcp": {
    "url": "http://localhost:5000/mcp"
  },
  "llm": {
    "provider": "ollama",
    "baseUrl": "http://localhost:11434",
    "chatPath": "/api/chat",
    "model": "qwen2.5:7b",
    "temperature": 0,
    "numCtx": 8192,
    "timeoutSegundos": 600
  },
  "chat": {
    "maxLlamadasPorPregunta": 8
  }
}
```

| Clave | Descripción |
|---|---|
| `mcp:url` | URL completa del servidor MCP (protocolo, host, puerto y ruta). |
| `llm:baseUrl` | URL de la API de Ollama. |
| `llm:chatPath` | Ruta del endpoint de chat. |
| `llm:model` | Nombre del modelo local. |
| `llm:temperature` | Temperatura del modelo; `0` da respuestas más deterministas. |
| `llm:numCtx` | Tamaño de la ventana de contexto. |
| `llm:timeoutSegundos` | Tiempo máximo de espera por respuesta del LLM. |
| `chat:maxLlamadasPorPregunta` | Límite de llamadas al LLM por pregunta, para evitar ciclos infinitos. |

### Cambiar la configuración sin editar archivos

```bash
# Servidor en otro puerto
dotnet run --project Servidor -- --Servidor:Puerto=5050

# Cliente con otro modelo y otra URL del servidor MCP
dotnet run --project Cliente -- --llm:model=llama3.1:8b --mcp:url=http://localhost:5050/mcp
```

Con variables de entorno se usa `__` como separador, por ejemplo `llm__model=llama3.1:8b` o `Servidor__Puerto=5050`.

## Ejecución

Usa tres terminales.

**1. Ollama** (si no está corriendo ya como servicio):

```bash
ollama serve
```

**2. Servidor MCP:**

```bash
cd Servidor
dotnet run
```

Para comprobar que está activo, abre `http://localhost:5000/` en el navegador.

**3. Cliente de chat:**

```bash
cd Cliente
dotnet run
```

Comandos dentro del chat:

| Comando | Acción |
|---|---|
| *(cualquier texto)* | Envía una pregunta al asistente. Se pueden hacer varias en la misma sesión. |
| `/nuevo` | Borra el historial e inicia una conversación nueva. |
| `/salir` | Termina el programa. |

Ejemplos de preguntas:

- `¿Cuántos multímetros hay disponibles?`
- `Préstale un taladro a Miguel.`
- `¿Qué préstamos tiene Carlos?`
- `Roberto devolvió la soldadora.`

## Datos de prueba

La primera vez que arranca, el servidor crea `Servidor/laboratorio.db` con estos datos:

| Id | Instrumento | Unidades |
|---|---|---|
| 1 | Taladro percutor | 5 |
| 2 | Multímetro digital | 8 |
| 3 | Llave dinamométrica | 3 |
| 4 | Soldadora inversora | 2 |
| 5 | Nivel láser | 4 |
| 6 | Esmeriladora angular | **0** (caso de prueba sin stock) |

Trabajadores: Carlos Mendoza, Laura Salazar, Roberto Gómez, Fernanda Ruiz y Miguel Torres.

Préstamos activos iniciales: Carlos tiene la llave dinamométrica, Roberto la soldadora y Fernanda el nivel láser.

Para volver a los datos iniciales, detén el servidor, borra `Servidor/laboratorio.db` y arráncalo de nuevo.

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

## Evidencias

Transcripciones reales de una ejecución con `qwen2.5:7b` en Ollama.

### 1. Servidor MCP iniciado y accesible en su URL

Salida del servidor al arrancar:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
 Servidor MCP de Laboratorio iniciado
 URL MCP: http://localhost:5000/mcp
```

Comprobación desde otra terminal: la ruta raíz responde, y el endpoint `/mcp` contesta el `initialize` del protocolo MCP.

```
$ curl http://localhost:5000/
Servidor MCP de Laboratorio activo. Endpoint MCP: /mcp

$ curl -X POST http://localhost:5000/mcp \
    -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
    -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"1"}}}'
event: message
data: {"result":{"protocolVersion":"2025-06-18","capabilities":{"logging":{},"tools":{}},"serverInfo":{"name":"Servidor","version":"1.0.0.0"}},"id":1,"jsonrpc":"2.0"}
```

### 2. Cliente mostrando la configuración efectiva

```
Configuración efectiva
  Modelo local: qwen2.5:7b
  API del LLM:  http://localhost:11434/api/chat
  Servidor MCP: http://localhost:5000/mcp
```

### 3. Cliente descubriendo las herramientas MCP (`ListToolsAsync`)

```
Conectando al servidor MCP...
Herramientas MCP descubiertas:
  - BuscarTrabajadores: Busca trabajadores del laboratorio por nombre o apellido, sin importar mayúsculas ni acentos. Devuelve id, nombre y apellido de cada coincidencia. ...
  - BuscarInstrumentos: Busca instrumentos del laboratorio por nombre, sin importar mayúsculas ni acentos. Devuelve id, nombre, unidades totales y unidades disponibles para préstamo. ...
  - RegistrarPrestamo: Registra el préstamo de un instrumento del laboratorio a un trabajador, con la fecha de hoy. Valida que el trabajador y el instrumento existan, que haya unidades disponibles y que el trabajador no haya llegado al máximo de préstamos activos permitido. ...
  - ConsultarPrestamosActivos: Lista los préstamos que todavía no se han devuelto, con su id, trabajador, instrumento, fecha y días prestado. ...
  - CompletarPrestamo: Registra la devolución de un instrumento: marca el préstamo como devuelto con la fecha de hoy. Valida que el préstamo exista y que no se haya devuelto antes. ...

Escribe tu pregunta. Comandos: /nuevo (nueva conversación), /salir.
```

### 4. Conversación en la que el modelo solicita herramientas y responde con base en su resultado

**Consulta con una sola herramienta.** El modelo pide `BuscarInstrumentos`, el cliente la ejecuta con `CallToolAsync` y, en la segunda llamada al LLM, el modelo responde con el dato devuelto.

```
Usuario: ¿Cuántos multímetros hay disponibles?

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 1)...
[MCP] El modelo solicita: BuscarInstrumentos
[MCP] Argumentos: {"texto":"multímetro"}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":true,"datos":[{"id":2,"nombre":"Multímetro digital","unidadesTotales":8,"unidadesDisponibles":8}]}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 2)...

Asistente: Actualmente hay 8 multímetros digitales disponibles para préstamo.
```

**Préstamo con varias herramientas encadenadas.** El modelo busca al trabajador y al instrumento para obtener sus ids, registra el préstamo y responde con el resultado final.

```
Usuario: Préstale un multímetro a Miguel.

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 1)...
[MCP] El modelo solicita: BuscarTrabajadores
[MCP] Argumentos: {"texto":"Miguel"}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":true,"datos":[{"id":5,"nombre":"Miguel","apellido":"Torres"}]}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 2)...
[MCP] El modelo solicita: BuscarInstrumentos
[MCP] Argumentos: {"texto":"multímetro"}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":true,"datos":[{"id":2,"nombre":"Multímetro digital","unidadesTotales":8,"unidadesDisponibles":8}]}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 3)...
[MCP] El modelo solicita: RegistrarPrestamo
[MCP] Argumentos: {"idTrabajador":5,"idInstrumento":2}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":true,"datos":{"idPrestamo":10,"trabajador":"Miguel Torres","instrumento":"Multímetro digital","fechaPrestamo":"2026-11-21","unidadesDisponiblesRestantes":7}}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 4)...

Asistente: El multímetro digital ha sido prestado a Miguel Torres con éxito. Quedan 7 unidades disponibles.
```

### 5. Validación y errores controlados de la herramienta

**Desde el chat.** El instrumento no existe: la herramienta devuelve `exito=false` con un mensaje claro y el modelo se lo explica al usuario sin inventar datos.

```
Usuario: ¿Hay osciloscopios en el laboratorio?

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 1)...
[MCP] El modelo solicita: BuscarInstrumentos
[MCP] Argumentos: {"texto":"osciloscopio"}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":false,"error":"No se encontró ningún instrumento que coincida con 'osciloscopio'. Prueba con otra palabra del nombre, o llama a BuscarInstrumentos sin texto para ver el inventario completo."}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 2)...

Asistente: No hay osciloscopios en el laboratorio. Puedes verificar el inventario completo si lo deseas.
```

**Llamada directa a `RegistrarPrestamo` por MCP (`tools/call`).** Así se ve la validación del servidor sin pasar por el LLM.

Instrumento sin unidades disponibles (Esmeriladora angular, id 6):

```json
{"exito":false,"error":"No hay unidades disponibles de 'Esmeriladora angular' en este momento (inventario total: 0, todas prestadas o sin stock)."}
```

Id inválido:

```json
{"exito":false,"error":"El id del trabajador debe ser un número mayor que 0 (se recibió 0). Si no conoces el id, búscalo primero con BuscarTrabajadores."}
```

## Solución de problemas

| Mensaje | Causa y solución |
|---|---|
| `No se pudo conectar a http://localhost:5000/mcp` | El servidor MCP no está corriendo, o `mcp:url` no coincide con el host, puerto o ruta del servidor. |
| `No se pudo comunicar con el LLM en http://localhost:11434/api/chat` | Ollama no está corriendo. Ejecuta `ollama serve` y verifica `llm:baseUrl`. |
| `404 ... model not found` | Falta descargar el modelo: `ollama pull qwen2.5:7b`. |
| Las respuestas tardan mucho | El modelo corre en CPU. La primera llamada incluye la carga del modelo; sube `llm:timeoutSegundos` si hace falta. |
| El asistente dice que hizo algo sin que aparezca la llamada `[MCP]` correspondiente | Los modelos pequeños a veces omiten la herramienta en peticiones que combinan varias acciones. Divide la petición en pasos o usa un modelo más grande (por ejemplo `qwen2.5:14b`), cambiando `llm:model`. |

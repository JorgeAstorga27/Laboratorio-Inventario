# Inventario de Laboratorio — Asistente con MCP y LLM local

Asistente de consola que administra los **préstamos de instrumentos de un laboratorio**. Un modelo de lenguaje local (Ollama) decide cuándo y con qué parámetros invocar las herramientas que expone un servidor **Model Context Protocol (MCP)** propio, escrito en C#/.NET.

```
usuario -> cliente/chat -> LLM local (Ollama) -> llamada MCP -> herramienta (SQLite) -> LLM local -> respuesta al usuario
```

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

### Cliente — `Cliente/config.json`

El repositorio ya incluye este archivo. Si falta, créalo a partir del ejemplo:

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

### Cambiar la configuración sin editar archivos

```bash
# Servidor en otro puerto
dotnet run --project Servidor -- --Servidor:Puerto=5050

# Cliente con otro modelo y otra URL del servidor MCP
dotnet run --project Cliente -- --llm:model=llama3.1:8b --mcp:url=http://localhost:5050/mcp
```

Con variables de entorno se usa `__` como separador, por ejemplo `llm__model=llama3.1:8b` o `Servidor__Puerto=5050`.

## Instalación y ejecución

### Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com/download)
- Un modelo con soporte de herramientas (*tool calling*). El proyecto se probó con `qwen2.5:7b`.

### Paso 1. Clonar el repositorio

```bash
git clone https://github.com/JorgeAstorga27/Laboratorio-Inventario.git
cd Laboratorio-Inventario
```

### Paso 2. Restaurar las dependencias de NuGet

```bash
dotnet restore Servidor/Servidor.csproj
dotnet restore Cliente/Cliente.csproj
```

Paquetes NuGet que se instalan:

| Proyecto | Paquetes |
|---|---|
| Servidor | `ModelContextProtocol`, `ModelContextProtocol.AspNetCore`, `Microsoft.Data.Sqlite` |
| Cliente | `ModelContextProtocol`, `Microsoft.Extensions.Configuration.Json`, `.EnvironmentVariables`, `.CommandLine` |

### Paso 3. Iniciar Ollama

Si Ollama ya está corriendo como servicio, omite este paso. Si no, ejecútalo en una terminal y déjala abierta:

```bash
ollama serve
```

### Paso 4. Descargar el modelo local

En otra terminal:

```bash
ollama pull qwen2.5:7b
```

### Paso 5. Ejecutar el servidor MCP

Desde la raíz del repositorio, en una terminal que debe quedar abierta:

```bash
cd Servidor
dotnet run
```

Para comprobar que está activo, abre `http://localhost:5000/` en el navegador.

### Paso 6. Ejecutar el cliente de chat

En otra terminal, desde la raíz del repositorio y con el servidor ya iniciado:

```bash
cd Cliente
dotnet run
```

### Uso del chat

Comandos disponibles:

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

## Estructura del proyecto

```
Laboratorio-Inventario/
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

Transcripciones de la ejecución del proyecto. 

### 1. Servidor MCP iniciado y accesible en su URL

Salida del servidor:

```text
PS C:\Users\jorge\Documents\PROYECTOS\Inventario-Laboratorio\Servidor> dotnet run
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Production
info: Microsoft.Hosting.Lifetime[0]
      Content root path: C:\Users\jorge\Documents\PROYECTOS\Inventario-Laboratorio\Servidor
 Servidor MCP de Laboratorio iniciado
 URL MCP: http://localhost:5000/mcp
```

Respuesta en el navegador al abrir `http://localhost:5000`:

```text
Servidor MCP de Laboratorio activo. Endpoint MCP: /mcp
```

### 2. Cliente mostrando el modelo, la URL del LLM y la URL del servidor MCP

```text
PS C:\Users\jorge\Documents\PROYECTOS\Inventario-Laboratorio\cliente> dotnet run
Configuración efectiva
  Modelo local: qwen2.5:7b
  API del LLM:  http://localhost:11434/api/chat
  Servidor MCP: http://localhost:5000/mcp
```

### 3. Cliente descubriendo las herramientas MCP

```text
Conectando al servidor MCP...
Herramientas MCP descubiertas:
  - BuscarTrabajadores: Busca trabajadores del laboratorio por nombre o apellido, sin importar mayúsculas ni acentos. Devuelve id, nombre y apellido de cada coincidencia. Úsala para obtener el id de un trabajador antes de registrar o consultar préstamos. Si hay varias coincidencias, pregunta al usuario cuál es.
  - BuscarInstrumentos: Busca instrumentos del laboratorio por nombre, sin importar mayúsculas ni acentos. Devuelve id, nombre, unidades totales y unidades disponibles para préstamo. Úsala para obtener el id de un instrumento o para saber si hay stock. Si hay varias coincidencias, pregunta al usuario cuál es.
  - RegistrarPrestamo: Registra el préstamo de un instrumento del laboratorio a un trabajador, con la fecha de hoy. Valida que el trabajador y el instrumento existan, que haya unidades disponibles y que el trabajador no haya llegado al máximo de préstamos activos permitido. Si no conoces el id del trabajador o del instrumento, búscalos primero por nombre; nunca los inventes.
  - ConsultarPrestamosActivos: Lista los préstamos que todavía no se han devuelto, con su id, trabajador, instrumento, fecha y días prestado. Úsala para obtener el id del préstamo antes de llamar a CompletarPrestamo, o para saber quién tiene un instrumento.
  - CompletarPrestamo: Registra la devolución de un instrumento: marca el préstamo como devuelto con la fecha de hoy. Valida que el préstamo exista y que no se haya devuelto antes. Si no conoces el id del préstamo, obténlo primero con ConsultarPrestamosActivos; nunca lo inventes.
```

### 4. Invocación de herramientas MCP y respuesta final

El modelo consulta el inventario, busca al trabajador, registra el préstamo y responde con el resultado.

```text
Usuario: ¿Hay algún multimetro disponible? ¿Cuantos hay? Prestale uno a Fernanda

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 1)...
[MCP] El modelo solicita: BuscarInstrumentos
[MCP] Argumentos: {"texto":"multímetro"}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":true,"datos":[{"id":2,"nombre":"Mult\u00EDmetro digital","unidadesTotales":8,"unidadesDisponibles":5}]}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 2)...
[MCP] El modelo solicita: BuscarTrabajadores
[MCP] Argumentos: {"texto":"Fernanda"}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":true,"datos":[{"id":4,"nombre":"Fernanda","apellido":"Ruiz"}]}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 3)...
[MCP] El modelo solicita: RegistrarPrestamo
[MCP] Argumentos: {"idTrabajador":4,"idInstrumento":2}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":true,"datos":{"idPrestamo":9,"trabajador":"Fernanda Ruiz","instrumento":"Mult\u00EDmetro digital","fechaPrestamo":"2026-11-23","unidadesDisponiblesRestantes":4}}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 4)...

Asistente: El préstamo ha sido registrado con éxito. Fernanda Ruiz ha prestado un multímetro digital. Ahora tiene 4 unidades disponibles en el inventario.
```

### 5. Validación y error controlado

**Validación del máximo de préstamos.** Fernanda ya tiene 3 préstamos activos, así que el servidor rechaza el registro.

```text
Usuario: Registra un prestamo a Fernanda de un taladro

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 1)...
[MCP] El modelo solicita: BuscarTrabajadores
[MCP] Argumentos: {"texto":"Fernanda"}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":true,"datos":[{"id":4,"nombre":"Fernanda","apellido":"Ruiz"}]}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 2)...
[MCP] El modelo solicita: BuscarInstrumentos
[MCP] Argumentos: {"texto":"taladro"}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":true,"datos":[{"id":1,"nombre":"Taladro percutor","unidadesTotales":5,"unidadesDisponibles":5}]}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 3)...
[MCP] El modelo solicita: RegistrarPrestamo
[MCP] Argumentos: {"idInstrumento":1,"idTrabajador":4}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":false,"error":"Fernanda Ruiz ya tiene 3 pr\u00E9stamos activos y el m\u00E1ximo permitido es 3. Debe devolver un instrumento antes de pedir otro."}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 4)...

Asistente: Lo siento, Fernanda Ruiz ya tiene el máximo de préstamos permitidos. Debe devolver un instrumento antes de pedir otro.
```

**Error controlado.** El trabajador no existe y la herramienta devuelve un mensaje comprensible.

```text
Usuario: Prestale al trabajador Abel un multimetro

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 1)...
[MCP] El modelo solicita: BuscarTrabajadores
[MCP] Argumentos: {"texto":"Abel"}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":false,"error":"No se encontr\u00F3 ning\u00FAn trabajador que coincida con \u0027Abel\u0027. Prueba con otra parte del nombre o el apellido, o llama a BuscarTrabajadores sin texto para ver la lista completa."}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 2)...

Asistente: No encontré ningún trabajador con el nombre o apellido "Abel". ¿Podrías proporcionar más información o verificar el nombre?
```

**Instrumento sin unidades.** El modelo verifica el inventario antes de registrar y no intenta el préstamo.

```text
Usuario: Hazle un prestamo de una esmeriladora a Carlos

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 1)...
[MCP] El modelo solicita: BuscarTrabajadores
[MCP] Argumentos: {"texto":"Carlos"}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":true,"datos":[{"id":1,"nombre":"Carlos","apellido":"Mendoza"}]}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 2)...
[MCP] El modelo solicita: BuscarInstrumentos
[MCP] Argumentos: {"texto":"esmeriladora"}
[MCP] Llamando al servidor MCP...
[MCP] Resultado: {"exito":true,"datos":[{"id":6,"nombre":"Esmeriladora angular","unidadesTotales":0,"unidadesDisponibles":0}]}

[LLM] Enviando historial y 5 herramientas a qwen2.5:7b (llamada 3)...

Asistente: No hay esmeriladoras disponibles en el momento. No se puede realizar el préstamo.
```

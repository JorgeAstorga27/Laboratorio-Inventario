using System.Globalization;
using Laboratorio.Datos;
using Laboratorio.Modelos;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Laboratorio.Servicios{
    //Los resultados  son: 
        // éxito + datos
        // mensaje de error

    public record Resultado(bool exito, object? datos, string? error)
    {
        public static Resultado Ok(object datos) => new(true, datos, null);
        public static Resultado Falla(string error) => new(false, null, error);
    }

    public class ServicioLaboratorio{
        private readonly RepositorioLaboratorio _repo;

        // Reglas configurables 
        private readonly int _maxPrestamos;

        public ServicioLaboratorio(RepositorioLaboratorio repo, IConfiguration config)
        {
            _repo = repo;
            _maxPrestamos = config.GetValue("Laboratorio:MaxPrestamos", 3);  //Un trabajador solo puede pedir 3 prestamos de instrumentos del laboratorio
        }

        //Utilidades
        private static DateTime Hoy => DateTime.Today;
        private static bool EstaActivo(Prestamo p) => p.fecha_devuelto is null;
        
        //Búsqueda sin importar mayúsculas ni acentos
        private static bool Contiene(string fuente, string texto) =>
            CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                fuente, texto, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

        //Servicios
        public Resultado RegistrarPrestamo(int idTrabajador, int idInstrumento)
        {
            //Validar entradas
            if (idTrabajador <= 0)
                return Resultado.Falla(
                    $"El id del trabajador debe ser un número mayor que 0 (se recibió {idTrabajador}). " +
                    "Si no conoces el id, búscalo primero con BuscarTrabajadores.");

            if (idInstrumento <= 0)
                return Resultado.Falla(
                    $"El id del instrumento debe ser un número mayor que 0 (se recibió {idInstrumento}). " +
                    "Si no conoces el id, búscalo primero con BuscarInstrumentos.");

            //Validar que existan
            var trabajador = _repo.ObtenerTrabajadores().FirstOrDefault(t => t.id == idTrabajador);
            if (trabajador is null)
                return Resultado.Falla(
                    $"No existe ningún trabajador con id {idTrabajador}. " +
                    "Búscalo con BuscarTrabajadores para obtener el id correcto.");

            var instrumento = _repo.ObtenerInstrumentos().FirstOrDefault(i => i.id == idInstrumento);
            if (instrumento is null)
                return Resultado.Falla(
                    $"No existe ningún instrumento con id {idInstrumento}. " +
                    "Búscalo con BuscarInstrumentos para obtener el id correcto.");

            //Reglas de negocio
            int prestamosActivos = _repo.ObtenerPrestamos()
                .Count(p => p.id_trabajador == idTrabajador && EstaActivo(p));

            if (prestamosActivos >= _maxPrestamos)
                return Resultado.Falla(
                    $"{trabajador.nombre} {trabajador.apellido} ya tiene {prestamosActivos} préstamos activos " +
                    $"y el máximo permitido es {_maxPrestamos}. Debe devolver un instrumento antes de pedir otro.");

            int disponibles = _repo.ConsultarNumeroInstrumentosDisponibles(idInstrumento);
            if (disponibles <= 0)
                return Resultado.Falla(
                    $"No hay unidades disponibles de '{instrumento.nombre}' en este momento " +
                    $"(inventario total: {instrumento.unidades_inventario}, todas prestadas o sin stock).");

            //Ejecutar
            try
            {
                int idPrestamo = _repo.InsertarPrestamo(idTrabajador, idInstrumento);

                return Resultado.Ok(new
                {
                    idPrestamo,
                    trabajador = $"{trabajador.nombre} {trabajador.apellido}",
                    instrumento = instrumento.nombre,
                    fechaPrestamo = Hoy.ToString("yyyy-MM-dd"),
                    unidadesDisponiblesRestantes = disponibles - 1
                });
            }
            catch (SqliteException)
            {
                return Resultado.Falla(
                    "Ocurrió un error al guardar el préstamo en la base de datos. Intenta de nuevo más tarde.");
            }
        }

        public Resultado CompletarPrestamo(int idPrestamo)
        {
            //Validar entradas
            if (idPrestamo <= 0)
                return Resultado.Falla(
                    $"El id del préstamo debe ser un número mayor que 0 (se recibió {idPrestamo}). " +
                    "Si no conoces el id, obténlo primero con ConsultarPrestamosActivos.");

            //Validar que exista
            var prestamo = _repo.ObtenerPrestamos().FirstOrDefault(p => p.id == idPrestamo);
            if (prestamo is null)
                return Resultado.Falla(
                    $"No existe ningún préstamo con id {idPrestamo}. " +
                    "Usa ConsultarPrestamosActivos para obtener el id correcto.");

            var trabajador = _repo.ObtenerTrabajadores().FirstOrDefault(t => t.id == prestamo.id_trabajador);
            var instrumento = _repo.ObtenerInstrumentos().FirstOrDefault(i => i.id == prestamo.id_instrumento);

            string nombreTrabajador = trabajador is null
                ? $"el trabajador {prestamo.id_trabajador}"
                : $"{trabajador.nombre} {trabajador.apellido}";
            string nombreInstrumento = instrumento?.nombre ?? $"el instrumento {prestamo.id_instrumento}";

            //Reglas de negocio
            if (!EstaActivo(prestamo))
                return Resultado.Falla(
                    $"El préstamo {idPrestamo} ('{nombreInstrumento}' de {nombreTrabajador}) ya había sido devuelto " +
                    $"el {prestamo.fecha_devuelto:yyyy-MM-dd}. No se realizó ningún cambio.");

            //Ejecutar
            try
            {
                bool actualizado = _repo.CompletarPrestamo(idPrestamo);
                if (!actualizado)
                    return Resultado.Falla(
                        $"No se pudo registrar la devolución del préstamo {idPrestamo}. " +
                        "Es posible que se haya devuelto al mismo tiempo; consulta el préstamo de nuevo.");

                return Resultado.Ok(new
                {
                    idPrestamo,
                    trabajador = nombreTrabajador,
                    instrumento = nombreInstrumento,
                    fechaPrestamo = prestamo.fecha_prestamo.ToString("yyyy-MM-dd"),
                    fechaDevuelto = Hoy.ToString("yyyy-MM-dd"),
                    diasPrestado = (Hoy - prestamo.fecha_prestamo.Date).Days
                });
            }
            catch (SqliteException)
            {
                return Resultado.Falla(
                    "Ocurrió un error al guardar la devolución en la base de datos. Intenta de nuevo más tarde.");
            }
        }

        private const int LongitudMaximaBusqueda = 100;

        public Resultado BuscarTrabajadores(string? texto)
        {
            //Validar entradas
            texto = texto?.Trim();
            if (texto is { Length: > LongitudMaximaBusqueda })
                return Resultado.Falla(
                    $"El texto de búsqueda es demasiado largo (máximo {LongitudMaximaBusqueda} caracteres). " +
                    "Usa solo el nombre o el apellido del trabajador.");

            //Buscar (sin texto se devuelven todos)
            var trabajadores = _repo.ObtenerTrabajadores()
                .Where(t => string.IsNullOrEmpty(texto)
                         || Contiene($"{t.nombre} {t.apellido}", texto))
                .Select(t => new { t.id, t.nombre, t.apellido })
                .ToList();

            if (trabajadores.Count == 0)
                return Resultado.Falla(
                    $"No se encontró ningún trabajador que coincida con '{texto}'. " +
                    "Prueba con otra parte del nombre o el apellido, o llama a BuscarTrabajadores sin texto para ver la lista completa.");

            return Resultado.Ok(trabajadores);
        }

        public Resultado BuscarInstrumentos(string? texto)
        {
            //Validar entradas
            texto = texto?.Trim();
            if (texto is { Length: > LongitudMaximaBusqueda })
                return Resultado.Falla(
                    $"El texto de búsqueda es demasiado largo (máximo {LongitudMaximaBusqueda} caracteres). " +
                    "Usa solo el nombre del instrumento o una parte de él.");

            //Buscar (sin texto se devuelven todos)
            var instrumentos = _repo.ObtenerInstrumentos()
                .Where(i => string.IsNullOrEmpty(texto) || Contiene(i.nombre, texto))
                .Select(i => new
                {
                    i.id,
                    i.nombre,
                    unidadesTotales = i.unidades_inventario,
                    unidadesDisponibles = _repo.ConsultarNumeroInstrumentosDisponibles(i.id)
                })
                .ToList();

            if (instrumentos.Count == 0)
                return Resultado.Falla(
                    $"No se encontró ningún instrumento que coincida con '{texto}'. " +
                    "Prueba con otra palabra del nombre, o llama a BuscarInstrumentos sin texto para ver el inventario completo.");

            return Resultado.Ok(instrumentos);
        }

        public Resultado ConsultarPrestamosActivos(int? idTrabajador)
        {
            //Validar entradas
            if (idTrabajador is <= 0)
                return Resultado.Falla(
                    $"El id del trabajador debe ser un número mayor que 0 (se recibió {idTrabajador}). " +
                    "Si no conoces el id, búscalo con BuscarTrabajadores, o no lo envíes para ver todos los préstamos activos.");

            var trabajadores = _repo.ObtenerTrabajadores().ToDictionary(t => t.id);
            var instrumentos = _repo.ObtenerInstrumentos().ToDictionary(i => i.id);

            //Validar que exista
            if (idTrabajador is not null && !trabajadores.ContainsKey(idTrabajador.Value))
                return Resultado.Falla(
                    $"No existe ningún trabajador con id {idTrabajador}. " +
                    "Búscalo con BuscarTrabajadores para obtener el id correcto.");

            //Consultar
            var activos = _repo.ObtenerPrestamos()
                .Where(p => EstaActivo(p) && (idTrabajador is null || p.id_trabajador == idTrabajador))
                .OrderBy(p => p.fecha_prestamo)
                .Select(p => new
                {
                    idPrestamo = p.id,
                    p.id_trabajador,
                    trabajador = trabajadores.TryGetValue(p.id_trabajador, out var t)
                        ? $"{t.nombre} {t.apellido}" : $"Trabajador {p.id_trabajador}",
                    p.id_instrumento,
                    instrumento = instrumentos.TryGetValue(p.id_instrumento, out var i)
                        ? i.nombre : $"Instrumento {p.id_instrumento}",
                    fechaPrestamo = p.fecha_prestamo.ToString("yyyy-MM-dd"),
                    diasPrestado = (Hoy - p.fecha_prestamo.Date).Days
                })
                .ToList();

            // Una lista vacía no es un error: se responde con éxito y un mensaje claro
            if (activos.Count == 0)
            {
                string mensaje = idTrabajador is null
                    ? "No hay préstamos activos en este momento: todos los instrumentos han sido devueltos."
                    : $"{trabajadores[idTrabajador.Value].nombre} {trabajadores[idTrabajador.Value].apellido} " +
                      "no tiene préstamos activos en este momento.";
                return Resultado.Ok(new { mensaje, prestamos = activos });
            }

            return Resultado.Ok(activos);
        }
    }
}
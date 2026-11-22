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
            _maxPrestamos = config.GetValue("Biblioteca:MaxPrestamos", 3);  //Un trabajador solo puede pedir 3 prestamos de instrumentos del laboratorio
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
                    "Si no conoces el id, busca primero al trabajador por su nombre.");

            if (idInstrumento <= 0)
                return Resultado.Falla(
                    $"El id del instrumento debe ser un número mayor que 0 (se recibió {idInstrumento}). " +
                    "Si no conoces el id, busca primero el instrumento por su nombre.");

            //Validar que existan
            var trabajador = _repo.ObtenerTrabajadores().FirstOrDefault(t => t.id == idTrabajador);
            if (trabajador is null)
                return Resultado.Falla(
                    $"No existe ningún trabajador con id {idTrabajador}. " +
                    "Busca al trabajador por su nombre para obtener el id correcto.");

            var instrumento = _repo.ObtenerInstrumentos().FirstOrDefault(i => i.id == idInstrumento);
            if (instrumento is null)
                return Resultado.Falla(
                    $"No existe ningún instrumento con id {idInstrumento}. " +
                    "Busca el instrumento por su nombre para obtener el id correcto.");

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
    }
}
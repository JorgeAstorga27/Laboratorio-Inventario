using System.ComponentModel;
using ModelContextProtocol.Server;
using Laboratorio.Servicios;

namespace Laboratorio.Herramientas{
    [McpServerToolType]
    public class HerramientasLaboratorio
    {
        private readonly ServicioLaboratorio _service;

        public HerramientasLaboratorio(ServicioLaboratorio service)
        {
            _service = service;
        }

        [McpServerTool(Name = "BuscarTrabajadores", ReadOnly = true)]
        [Description("Busca trabajadores del laboratorio por nombre o apellido, sin importar mayúsculas ni acentos. " +
                     "Devuelve id, nombre y apellido de cada coincidencia. Úsala para obtener el id de un trabajador " +
                     "antes de registrar o consultar préstamos. Si hay varias coincidencias, pregunta al usuario cuál es.")]
        public Resultado BuscarTrabajadores(
            [Description("Nombre, apellido o parte de ellos, ej. 'Carlos' o 'Mendoza'. Déjalo vacío para listar a todos los trabajadores.")]
            string? texto = null)
            => _service.BuscarTrabajadores(texto);

        [McpServerTool(Name = "BuscarInstrumentos", ReadOnly = true)]
        [Description("Busca instrumentos del laboratorio por nombre, sin importar mayúsculas ni acentos. " +
                     "Devuelve id, nombre, unidades totales y unidades disponibles para préstamo. Úsala para obtener " +
                     "el id de un instrumento o para saber si hay stock. Si hay varias coincidencias, pregunta al usuario cuál es.")]
        public Resultado BuscarInstrumentos(
            [Description("Nombre o parte del nombre del instrumento, ej. 'taladro' o 'multímetro'. Déjalo vacío para ver todo el inventario.")]
            string? texto = null)
            => _service.BuscarInstrumentos(texto);

        [McpServerTool(Name = "RegistrarPrestamo")]
        [Description("Registra el préstamo de un instrumento del laboratorio a un trabajador, con la fecha de hoy. " +
                     "Valida que el trabajador y el instrumento existan, que haya unidades disponibles y que el trabajador " +
                     "no haya llegado al máximo de préstamos activos permitido. " +
                     "Si no conoces el id del trabajador o del instrumento, búscalos primero por nombre; nunca los inventes.")]
        public Resultado RegistrarPrestamo(
            [Description("Id numérico del trabajador que pide el instrumento, ej. 1. Obtenlo con BuscarTrabajadores. No lo inventes.")]
            int idTrabajador,
            [Description("Id numérico del instrumento a prestar, ej. 3. Obtenlo con BuscarInstrumentos. No lo inventes.")]
            int idInstrumento)
            => _service.RegistrarPrestamo(idTrabajador, idInstrumento);

        [McpServerTool(Name = "ConsultarPrestamosActivos", ReadOnly = true)]
        [Description("Lista los préstamos que todavía no se han devuelto, con su id, trabajador, instrumento, fecha y días prestado. " +
                     "Úsala para obtener el id del préstamo antes de llamar a CompletarPrestamo, o para saber quién tiene un instrumento.")]
        public Resultado ConsultarPrestamosActivos(
            [Description("Id numérico del trabajador, obtenido con BuscarTrabajadores. Déjalo vacío para ver los préstamos activos de todos.")]
            int? idTrabajador = null)
            => _service.ConsultarPrestamosActivos(idTrabajador);

        [McpServerTool(Name = "CompletarPrestamo")]
        [Description("Registra la devolución de un instrumento: marca el préstamo como devuelto con la fecha de hoy. " +
                     "Valida que el préstamo exista y que no se haya devuelto antes. " +
                     "Si no conoces el id del préstamo, obténlo primero con ConsultarPrestamosActivos; nunca lo inventes.")]
        public Resultado CompletarPrestamo(
            [Description("Id numérico del préstamo a cerrar, ej. 3. Obtenlo con ConsultarPrestamosActivos. No lo inventes.")]
            int idPrestamo)
            => _service.CompletarPrestamo(idPrestamo);
    }
}
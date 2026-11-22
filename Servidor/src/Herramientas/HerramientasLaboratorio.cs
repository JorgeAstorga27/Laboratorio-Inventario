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
    }
}
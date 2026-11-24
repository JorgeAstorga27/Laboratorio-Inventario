namespace Laboratorio.Modelos
{
    
    public class Instrumento
    {
        public int id {get; set;}
        public string nombre {get; set;} = string.Empty;
        public int unidades_inventario{get; set;}
        
    }

    public class Trabajador
    {
        public int id {get; set;}
        public string nombre {get; set;} = string.Empty;
        public string apellido {get; set;} = string.Empty;

    }    

    public class Prestamo
    {
        public int id {get; set;}
        public int id_trabajador {get; set;}
        public int id_instrumento {get; set;}
        public DateTime fecha_prestamo {get; set;}
        public DateTime? fecha_devuelto {get; set;} //Si no se ha devuelto, la fecha es nula

    }
}
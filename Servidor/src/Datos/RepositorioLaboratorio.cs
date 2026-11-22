using Laboratorio.Modelos;
using Microsoft.Data.Sqlite;

namespace Laboratorio.Datos
{
    public class RepositorioLaboratorio
    {

        //Creación de la BD y datos iniciales
        private readonly string _cadenaConexion = "Data Source=laboratorio.db";

        public RepositorioLaboratorio(){
            InicializarBaseDeDatos();
        }

        private SqliteConnection ObtenerConexion(){
            var conexion = new SqliteConnection(_cadenaConexion);
            conexion.Open();

            using var comando = new SqliteCommand(
                "PRAGMA foreign_keys = ON;", conexion);

            comando.ExecuteNonQuery();

            return conexion;
        }

        public void InicializarBaseDeDatos()
        {
             using var conexion = ObtenerConexion();

            string tablas = @"
                CREATE TABLE IF NOT EXISTS INSTRUMENTO(
                    id integer PRIMARY KEY,
                    nombre text NOT NULL,
                    unidades_inventario integer NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS TRABAJADOR(
                    id integer PRIMARY KEY,
                    nombre text NOT NULL,
                    apellido text NOT NULL
                );

                CREATE TABLE IF NOT EXISTS PRESTAMO(
                    id integer PRIMARY KEY,
                    id_trabajador integer NOT NULL,
                    id_instrumento integer NOT NULL,
                    fecha_prestamo TEXT NOT NULL,
                    fecha_devuelto TEXT NULL,
                    FOREIGN KEY (id_trabajador) REFERENCES TRABAJADOR(id),
                    FOREIGN KEY (id_instrumento) REFERENCES INSTRUMENTO(id)
                );
            ";

            using var comando = new SqliteCommand(tablas, conexion);
            comando.ExecuteNonQuery();

            CargarDatosIniciales(conexion);
        }

        public void CargarDatosIniciales(SqliteConnection conexion)
        {
            //VERIFICAR SI YA HAY DATOS
            using var cmdVerificar = new SqliteCommand("SELECT COUNT(*) FROM INSTRUMENTO;", conexion);
            if ((long)cmdVerificar.ExecuteScalar()! > 0)
                return;

           string seed = @"
                --Instrumentos
                INSERT INTO INSTRUMENTO (nombre, unidades_inventario) VALUES
                ('Taladro percutor', 5),
                ('Multímetro digital', 8),
                ('Llave dinamométrica', 3),
                ('Soldadora inversora', 2),
                ('Nivel láser', 4),
                ('Esmeriladora angular', 0); -- Caso de prueba: sin stock

                --Trabajadores
                INSERT INTO TRABAJADOR (nombre, apellido) VALUES
                ('Carlos', 'Mendoza'),
                ('Laura', 'Salazar'),
                ('Roberto', 'Gómez'),
                ('Fernanda', 'Ruiz'),
                ('Miguel', 'Torres');

                --Préstamos
                INSERT INTO PRESTAMO (id_trabajador, id_instrumento, fecha_prestamo, fecha_devuelto) VALUES
                (1, 1, '2026-11-01', '2026-11-05'), -- Carlos pidió Taladro y ya lo devolvió
                (2, 2, '2026-11-10', '2026-11-12'), -- Laura pidió Multímetro y ya lo devolvió
                (1, 3, '2026-11-18', NULL),         -- Carlos tiene una Llave sin devolver
                (3, 4, '2026-11-19', NULL),         -- Roberto tiene la Soldadora sin devolver
                (4, 5, '2026-11-20', NULL);         -- Fernanda tiene el Nivel láser sin devolver
            ";

            using var cmdSeed = new SqliteCommand(seed, conexion);
            cmdSeed.ExecuteNonQuery();
        }
    
        //Metodos para la gestión del inventario del laboratorio

        //Metodos para instrumentos
        public List<Instrumento> ObtenerInstrumentos()
            {
                var lista = new List<Instrumento>();

                using var conexion = ObtenerConexion();

                string query = "SELECT * FROM INSTRUMENTO;";

                using var comando = new SqliteCommand(query, conexion);
                using var reader = comando.ExecuteReader();

                while (reader.Read())
                {
                    lista.Add(new Instrumento
                    {
                        id = reader.GetInt32(0),
                        nombre = reader.GetString(1),
                        unidades_inventario = reader.GetInt32(2)
                    });
                }

                return lista;
            }

        public List<Instrumento> ObtenerInstrumentosPorNombre(string filtro)
            {
                var lista = new List<Instrumento>();

                using var conexion = ObtenerConexion();

                string query = "SELECT * FROM INSTRUMENTO WHERE nombre LIKE @filtro;";

                using var comando = new SqliteCommand(query, conexion);
                comando.Parameters.AddWithValue("@filtro", $"%{filtro}%");

                using var reader = comando.ExecuteReader();

                while (reader.Read())
                {
                    lista.Add(new Instrumento
                    {
                        id = reader.GetInt32(0),
                        nombre = reader.GetString(1),
                        unidades_inventario = reader.GetInt32(2)
                    });
                }

                return lista;
            }
   
        //Metodos para trabajadores

        public List <Trabajador> ObtenerTrabajadores()
        {
            var lista = new List<Trabajador>();

            using var conexion = ObtenerConexion();

            string query = "SELECT * FROM TRABAJADOR;";

            using var comando = new SqliteCommand(query, conexion);
            using var reader = comando.ExecuteReader();

            while (reader.Read())
            {
                lista.Add(new Trabajador
                {
                    id = reader.GetInt32(0),
                    nombre = reader.GetString(1),
                    apellido = reader.GetString(2)
                });
            }

            return lista;
        }
    
        public List<Trabajador> ObtenerTrabajadorPorNombre(string filtro)
            {
                var lista = new List<Trabajador>();

                using var conexion = ObtenerConexion();

                string query = "SELECT * FROM TRABAJADOR WHERE nombre LIKE @filtro OR apellido LIKE @filtro";

                using var comando = new SqliteCommand(query, conexion);
                comando.Parameters.AddWithValue("@filtro", $"%{filtro}%");

                using var reader = comando.ExecuteReader();

                while (reader.Read())
                {
                    lista.Add(new Trabajador
                    {
                        id = reader.GetInt32(0),
                        nombre = reader.GetString(1),
                        apellido = reader.GetString(2)
                    });
                }

                return lista;
            }
   
        //Metodos para prestamos
        public List <Prestamo> ObtenerPrestamos()
        {
            var lista = new List<Prestamo>();

            using var conexion = ObtenerConexion();

            string query = "SELECT id, id_trabajador, id_instrumento, fecha_prestamo, fecha_devuelto FROM PRESTAMO;";

            using var comando = new SqliteCommand(query, conexion);
            using var reader = comando.ExecuteReader();

            while (reader.Read())
            {
                lista.Add(new Prestamo
                {
                    id = reader.GetInt32(0),
                    id_trabajador = reader.GetInt32(1),
                    id_instrumento = reader.GetInt32(2),
                    fecha_prestamo = DateTime.Parse(reader.GetString(3)),
                    fecha_devuelto = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4))
                });
            }
            return lista;
        }
        
        public int ConsultarNumeroInstrumentosDisponibles(int id)
        {
            using var conexion = ObtenerConexion();

            string query = @"
                SELECT i.unidades_inventario - COUNT(p.id) AS disponibles
                FROM INSTRUMENTO i
                LEFT JOIN PRESTAMO p
                    ON p.id_instrumento = i.id
                   AND p.fecha_devuelto IS NULL
                WHERE i.id = @id
                GROUP BY i.id, i.unidades_inventario;";

            using var comando = new SqliteCommand(query, conexion);
            comando.Parameters.AddWithValue("@id", id);

            var resultado = comando.ExecuteScalar();

            // Si el instrumento no existe, la consulta no devuelve filas
            if (resultado == null || resultado == DBNull.Value)
                return 0;

            return Convert.ToInt32(resultado);
        }

        public int InsertarPrestamo(int idTrabajador, int idInstrumento)
        {
            using var conexion = ObtenerConexion();

            string query = @"
                INSERT INTO PRESTAMO (id_trabajador, id_instrumento, fecha_prestamo, fecha_devuelto)
                VALUES (@idTrabajador, @idInstrumento, @fechaPrestamo, NULL);
                SELECT last_insert_rowid();";

            using var comando = new SqliteCommand(query, conexion);
            comando.Parameters.AddWithValue("@idTrabajador", idTrabajador);
            comando.Parameters.AddWithValue("@idInstrumento", idInstrumento);
            comando.Parameters.AddWithValue("@fechaPrestamo", DateTime.Now.ToString("yyyy-MM-dd"));

            return Convert.ToInt32(comando.ExecuteScalar());
        }

        public bool CompletarPrestamo(int idPrestamo)
        {
            using var conexion = ObtenerConexion();

            string query = @"
                UPDATE PRESTAMO
                SET fecha_devuelto = @fechaDevuelto
                WHERE id = @id
                  AND fecha_devuelto IS NULL;";

            using var comando = new SqliteCommand(query, conexion);
            comando.Parameters.AddWithValue("@fechaDevuelto", DateTime.Now.ToString("yyyy-MM-dd"));
            comando.Parameters.AddWithValue("@id", idPrestamo);

            return comando.ExecuteNonQuery() > 0;
        }
    }
}
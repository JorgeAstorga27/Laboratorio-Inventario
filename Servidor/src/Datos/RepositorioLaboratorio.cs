using Microsoft.Data.Sqlite;

namespace Laboratorio.Datos
{
    public class LaboratorioRepositorio
    {

        //Creación de la BD y datos iniciales
        private readonly string _cadenaConexion = "Data Source=laboratorio.db";

        public LaboratorioRepositorio(){
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
    }
}
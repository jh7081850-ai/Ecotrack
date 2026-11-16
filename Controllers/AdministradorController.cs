using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;

namespace Integradora.Controllers
{
    public class AdminController : Controller
    {
        private readonly IConfiguration _configuration;

        public AdminController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Index()
        {
            string nombre = HttpContext.Session.GetString("UsuarioNombre") ?? "Octavio Perez";
            string iniciales = HttpContext.Session.GetString("UsuarioIniciales") ?? "OP";
            string rol = HttpContext.Session.GetString("UsuarioRol") ?? "Administrador";

            ViewBag.UsuarioNombre = nombre;
            ViewBag.UsuarioIniciales = iniciales;
            ViewBag.UsuarioRol = rol;

            var incidencias = new List<dynamic>();
            string connectionString = _configuration.GetConnectionString("NeonDb");

            try
            {
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();
                    ViewBag.EstadoConexion = "Conectado";

                    using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM usuarios;", conn))
                        ViewBag.TotalUsuarios = Convert.ToInt32(cmd.ExecuteScalar());

                    using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM retos_ecologicos WHERE LOWER(estado) = 'activo';", conn))
                        ViewBag.RetosActivos = Convert.ToInt32(cmd.ExecuteScalar());

                    const string sqlSinAtender = @"
                        SELECT COUNT(*) 
                        FROM reportes r
                        LEFT JOIN estados_reporte e ON r.id_estado = e.id_estado
                        WHERE LOWER(COALESCE(e.nombre, '')) = 'pendiente' OR r.id_estado = 1 OR r.id_estado IS NULL;";
                    using (var cmd = new NpgsqlCommand(sqlSinAtender, conn))
                        ViewBag.ReportesSinAtender = Convert.ToInt32(cmd.ExecuteScalar());

                    
                    const string sqlReportes = @"
                        SELECT r.id_reporte, 
                               r.titulo, 
                               COALESCE(r.descripcion, '') AS descripcion, 
                               r.latitud, 
                               r.longitud, 
                               COALESCE(e.nombre, 'Pendiente') AS estatus,
                               COALESCE(u.nombre, 'Ciudadano') AS reportante,
                               COALESCE(r.id_usuario, 0) AS id_usuario
                        FROM reportes r
                        LEFT JOIN estados_reporte e ON r.id_estado = e.id_estado
                        LEFT JOIN usuarios u ON r.id_usuario = u.id_usuario
                        ORDER BY r.id_reporte DESC;";

                    using (var cmd = new NpgsqlCommand(sqlReportes, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            incidencias.Add(new
                            {
                                Id = reader["id_reporte"],
                                Titulo = reader["titulo"]?.ToString() ?? "Sin título",
                                Descripcion = reader["descripcion"]?.ToString() ?? "",
                                Coordenadas = $"{Convert.ToDouble(reader["latitud"]):F4}, {Convert.ToDouble(reader["longitud"]):F4}",
                                Estatus = reader["estatus"]?.ToString() ?? "Pendiente",
                                IdUsuario = Convert.ToInt32(reader["id_usuario"]),
                                Reportante = reader["reportante"].ToString()
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.EstadoConexion = "Error: " + ex.Message;
                ViewBag.TotalUsuarios = 0;
                ViewBag.ReportesSinAtender = 0;
                ViewBag.RetosActivos = 0;
            }

            ViewBag.Incidencias = incidencias;
            return View();
        }

        
        [HttpGet]
        public IActionResult Usuarios()
        {
            CargarSesion();
            var lista = new List<dynamic>();
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                const string sql = @"SELECT id_usuario, nombre, correo, rol, COALESCE(puntos_totales, 0) AS puntos, fecha_registro FROM usuarios ORDER BY id_usuario DESC;";
                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new
                        {
                            Id = reader["id_usuario"],
                            Nombre = reader["nombre"]?.ToString() ?? "Sin nombre",
                            Correo = reader["correo"]?.ToString() ?? "Sin correo",
                            Rol = reader["rol"]?.ToString() ?? "Ciudadano",
                            Puntos = reader["puntos"],
                            Fecha = reader["fecha_registro"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_registro"]).ToString("dd/MM/yyyy") : "Registrado"
                        });
                    }
                }
            }
            ViewBag.Usuarios = lista;
            return View();
        }

    
        [HttpGet]
        public IActionResult Retos()
        {
            CargarSesion();
            var lista = new List<dynamic>();
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                const string sql = @"SELECT id_reto, id_reporte_origen, titulo, descripcion, fecha_inicio, fecha_fin, puntos_recompensa, estado FROM retos_ecologicos ORDER BY id_reto DESC;";
                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new
                        {
                            Id = reader["id_reto"],
                            IdReporteOrigen = reader["id_reporte_origen"] != DBNull.Value ? reader["id_reporte_origen"].ToString() : "Directo",
                            Titulo = reader["titulo"]?.ToString() ?? "Sin título",
                            Descripcion = reader["descripcion"]?.ToString() ?? "",
                            Puntos = reader["puntos_recompensa"],
                            Estado = reader["estado"]?.ToString() ?? "Activo",
                            Inicio = reader["fecha_inicio"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_inicio"]).ToString("dd/MM/yyyy") : "-",
                            Fin = reader["fecha_fin"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_fin"]).ToString("dd/MM/yyyy") : "-"
                        });
                    }
                }
            }
            ViewBag.Retos = lista;
            return View();
        }

        // 3. Registro de Participantes
        [HttpGet]
        public IActionResult Participantes()
        {
            CargarSesion();
            var lista = new List<dynamic>();
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                const string sql = @"SELECT pr.id_participacion, u.nombre AS ciudadano, r.titulo AS reto, pr.rol_en_reto, pr.completo_reto, pr.fecha_inscripcion 
                                     FROM participantes_retos pr
                                     INNER JOIN usuarios u ON pr.id_usuario = u.id_usuario
                                     INNER JOIN retos_ecologicos r ON pr.id_reto = r.id_reto
                                     ORDER BY pr.id_participacion DESC;";
                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        bool comp = reader["completo_reto"] != DBNull.Value && Convert.ToBoolean(reader["completo_reto"]);
                        lista.Add(new
                        {
                            Id = reader["id_participacion"],
                            Ciudadano = reader["ciudadano"]?.ToString() ?? "Sin nombre",
                            Reto = reader["reto"]?.ToString() ?? "Sin título",
                            Rol = reader["rol_en_reto"]?.ToString() ?? "Voluntario",
                            Completo = comp,
                            Estatus = comp ? "Acreditado" : "En Progreso",
                            Fecha = reader["fecha_inscripcion"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_inscripcion"]).ToString("dd/MM/yyyy HH:mm") : "N/A"
                        });
                    }
                }
            }
            ViewBag.Participantes = lista;
            return View();
        }

        [HttpGet]
        public IActionResult Puntos()
        {
            CargarSesion();
            var lista = new List<dynamic>();
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                const string sql = @"SELECT hp.id_transaccion, u.nombre AS ciudadano, hp.puntos, hp.motivo, hp.fecha, r.titulo AS reto_asociado
                                     FROM historial_puntos hp
                                     INNER JOIN usuarios u ON hp.id_usuario = u.id_usuario
                                     LEFT JOIN retos_ecologicos r ON hp.id_reto = r.id_reto
                                     ORDER BY hp.id_transaccion DESC;";
                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new
                        {
                            Id = reader["id_transaccion"],
                            Ciudadano = reader["ciudadano"]?.ToString() ?? "Sin nombre",
                            Puntos = reader["puntos"],
                            Motivo = reader["motivo"]?.ToString() ?? "Abono ecológico",
                            Reto = reader["reto_asociado"]?.ToString() ?? "Reporte Directo",
                            Fecha = reader["fecha"] != DBNull.Value ? Convert.ToDateTime(reader["fecha"]).ToString("dd/MM/yyyy HH:mm") : "N/A"
                        });
                    }
                }
            }
            ViewBag.Transacciones = lista;
            return View();
        }

        [HttpPost]
        public IActionResult ActualizarEstatusYAsignarPuntos(int id_reporte, string nuevo_estatus, int id_usuario, int puntos, string motivo)
        {
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();

                int idEstado = nuevo_estatus == "Resuelto" ? 3 : (nuevo_estatus == "En Proceso" ? 2 : 1);

                const string sqlUpdate = @"UPDATE reportes SET id_estado = @est, fecha_actualizacion = NOW() WHERE id_reporte = @idRep;";
                using (var cmd = new NpgsqlCommand(sqlUpdate, conn))
                {
                    cmd.Parameters.AddWithValue("@est", idEstado);
                    cmd.Parameters.AddWithValue("@idRep", id_reporte);
                    cmd.ExecuteNonQuery();
                }

                if (puntos > 0 && id_usuario > 0)
                {
                    const string sqlPuntos = @"INSERT INTO historial_puntos (id_usuario, id_reto, puntos, motivo, fecha) 
                                               VALUES (@usr, NULL, @pts, @mot, NOW());";
                    using (var cmdP = new NpgsqlCommand(sqlPuntos, conn))
                    {
                        cmdP.Parameters.AddWithValue("@usr", id_usuario);
                        cmdP.Parameters.AddWithValue("@pts", puntos);
                        cmdP.Parameters.AddWithValue("@mot", string.IsNullOrEmpty(motivo) ? $"Atención de reporte #{id_reporte}" : motivo);
                        cmdP.ExecuteNonQuery();
                    }

                    const string sqlSum = "UPDATE usuarios SET puntos_totales = COALESCE(puntos_totales, 0) + @pts WHERE id_usuario = @usr;";
                    using (var cmdS = new NpgsqlCommand(sqlSum, conn))
                    {
                        cmdS.Parameters.AddWithValue("@pts", puntos);
                        cmdS.Parameters.AddWithValue("@usr", id_usuario);
                        cmdS.ExecuteNonQuery();
                    }
                }
            }

            return RedirectToAction("Index");
        }

        private void CargarSesion()
        {
            ViewBag.UsuarioNombre = HttpContext.Session.GetString("UsuarioNombre") ?? "Administrador";
            ViewBag.UsuarioIniciales = HttpContext.Session.GetString("UsuarioIniciales") ?? "AD";
            ViewBag.UsuarioRol = HttpContext.Session.GetString("UsuarioRol") ?? "Administrador";
        }
    }
}
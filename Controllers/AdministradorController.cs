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
                        WHERE LOWER(COALESCE(e.nombre, '')) IN ('pendiente', 'por validar') OR r.id_estado = 1 OR r.id_estado IS NULL;";
                    using (var cmd = new NpgsqlCommand(sqlSinAtender, conn))
                        ViewBag.ReportesSinAtender = Convert.ToInt32(cmd.ExecuteScalar());

                    const string sqlReportes = @"
                        SELECT r.id_reporte, 
                               r.titulo, 
                               COALESCE(r.descripcion, '') AS descripcion, 
                               r.latitud, 
                               r.longitud, 
                               COALESCE(e.nombre, 'Por Validar') AS estatus,
                               COALESCE(u.nombre, 'Ciudadano') AS reportante,
                               COALESCE(r.id_usuario, 0) AS id_usuario,
                               COALESCE(re.estado, 'Por Validar') AS estado_reto,
                               COALESCE(
                                   (SELECT ef.url_foto FROM evidencias_fotograficas ef 
                                    WHERE ef.id_reporte = r.id_reporte 
                                      AND LOWER(COALESCE(ef.tipo_evidencia, '')) NOT LIKE '%soluci%'
                                    ORDER BY ef.id_evidencia ASC LIMIT 1), 
                               '') AS foto_inicial,
                               COALESCE(
                                   (SELECT ef.url_foto FROM evidencias_fotograficas ef 
                                    WHERE ef.id_reporte = r.id_reporte 
                                      AND (LOWER(COALESCE(ef.tipo_evidencia, '')) LIKE '%soluci%' 
                                           OR ef.id_evidencia > (SELECT MIN(ef2.id_evidencia) FROM evidencias_fotograficas ef2 WHERE ef2.id_reporte = r.id_reporte))
                                    ORDER BY ef.id_evidencia DESC LIMIT 1), 
                               '') AS foto_solucion,
                               COALESCE(
                                   (SELECT u2.nombre FROM participantes_retos pr 
                                    INNER JOIN usuarios u2 ON pr.id_usuario = u2.id_usuario 
                                    WHERE pr.id_reto = re.id_reto AND pr.completo_reto = TRUE 
                                    LIMIT 1), 
                               'Voluntario Comunitario') AS solucionador
                        FROM reportes r
                        LEFT JOIN estados_reporte e ON r.id_estado = e.id_estado
                        LEFT JOIN usuarios u ON r.id_usuario = u.id_usuario
                        LEFT JOIN retos_ecologicos re ON r.id_reporte = re.id_reporte_origen
                        ORDER BY r.id_reporte DESC;";

                    using (var cmd = new NpgsqlCommand(sqlReportes, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string fIni = reader["foto_inicial"]?.ToString() ?? "";
                            string fSol = reader["foto_solucion"]?.ToString() ?? "";

                            incidencias.Add(new
                            {
                                Id = reader["id_reporte"],
                                Titulo = reader["titulo"]?.ToString() ?? "Sin título",
                                Descripcion = reader["descripcion"]?.ToString() ?? "",
                                Coordenadas = $"{Convert.ToDouble(reader["latitud"]):F4}, {Convert.ToDouble(reader["longitud"]):F4}",
                                Estatus = reader["estatus"]?.ToString() ?? "Por Validar",
                                EstadoReto = reader["estado_reto"]?.ToString() ?? "Por Validar",
                                IdUsuario = Convert.ToInt32(reader["id_usuario"]),
                                Reportante = reader["reportante"]?.ToString() ?? "Ciudadano",
                                FotoInicial = fIni,
                                FotoSolucion = string.IsNullOrEmpty(fSol) ? fIni : fSol,
                                Solucionador = reader["solucionador"]?.ToString() ?? "Voluntario Comunitario"
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

        // 2. Catálogo de Retos (Incluye Estado de Validación y Evidencias Fotográficas Antes/Después)
        [HttpGet]
        public IActionResult Retos()
        {
            CargarSesion();
            var lista = new List<dynamic>();
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                const string sql = @"
                    SELECT re.id_reto, 
                           re.id_reporte_origen, 
                           re.titulo, 
                           re.descripcion, 
                           re.fecha_inicio, 
                           re.fecha_fin, 
                           re.puntos_recompensa, 
                           re.estado,
                           COALESCE(
                               (SELECT ef.url_foto FROM evidencias_fotograficas ef 
                                WHERE ef.id_reporte = re.id_reporte_origen 
                                  AND LOWER(COALESCE(ef.tipo_evidencia, '')) NOT LIKE '%solucion%'
                                ORDER BY ef.id_evidencia ASC LIMIT 1), 
                           '') AS foto_antes,
                           COALESCE(
                               (SELECT ef.url_foto FROM evidencias_fotograficas ef 
                                WHERE ef.id_reporte = re.id_reporte_origen 
                                  AND (LOWER(COALESCE(ef.tipo_evidencia, '')) LIKE '%solucion%' 
                                       OR ef.id_evidencia > (SELECT MIN(ef2.id_evidencia) FROM evidencias_fotograficas ef2 WHERE ef2.id_reporte = re.id_reporte_origen))
                                ORDER BY ef.id_evidencia DESC LIMIT 1), 
                           '') AS foto_solucion,
                           COALESCE(
                               (SELECT u.nombre FROM participantes_retos pr 
                                INNER JOIN usuarios u ON pr.id_usuario = u.id_usuario 
                                WHERE pr.id_reto = re.id_reto AND pr.completo_reto = TRUE 
                                LIMIT 1), 
                           'Voluntario Comunitario') AS solucionador
                    FROM retos_ecologicos re 
                    ORDER BY re.id_reto DESC;";

                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string fAntes = reader["foto_antes"]?.ToString() ?? "";
                        string fSolucion = reader["foto_solucion"]?.ToString() ?? "";

                        // Si solo hay una foto registrada y el reto ya está completado, la mostramos como respaldo
                        if (string.IsNullOrEmpty(fSolucion) && !string.IsNullOrEmpty(fAntes) &&
                            (reader["estado"]?.ToString().ToLower() == "completado" || reader["estado"]?.ToString().ToLower() == "resuelto"))
                        {
                            fSolucion = fAntes;
                        }

                        lista.Add(new
                        {
                            Id = reader["id_reto"],
                            IdReporteOrigen = reader["id_reporte_origen"] != DBNull.Value ? reader["id_reporte_origen"].ToString() : "Directo",
                            IdReporteNum = reader["id_reporte_origen"] != DBNull.Value ? Convert.ToInt32(reader["id_reporte_origen"]) : 0,
                            Titulo = reader["titulo"]?.ToString() ?? "Sin título",
                            Descripcion = reader["descripcion"]?.ToString() ?? "",
                            Puntos = reader["puntos_recompensa"],
                            Estado = reader["estado"]?.ToString() ?? "Por Validar",
                            Inicio = reader["fecha_inicio"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_inicio"]).ToString("dd/MM/yyyy") : "-",
                            Fin = reader["fecha_fin"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_fin"]).ToString("dd/MM/yyyy") : "-",
                            FotoAntes = fAntes,
                            FotoSolucion = fSolucion,
                            Solucionador = reader["solucionador"]?.ToString() ?? "Voluntario Comunitario"
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

        // NUEVO: Validar incidencia y publicar su reto ecológico en el mapa
        [HttpPost]
        public IActionResult ValidarYPublicarEnMapa(int id_reporte)
        {
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();

                // 1. Pasa el reporte a estado 2 (En Proceso / Activo en el mapa)
                const string sqlRep = @"UPDATE reportes SET id_estado = 2, fecha_actualizacion = NOW() WHERE id_reporte = @idRep;";
                using (var cmd = new NpgsqlCommand(sqlRep, conn))
                {
                    cmd.Parameters.AddWithValue("@idRep", id_reporte);
                    cmd.ExecuteNonQuery();
                }

                // 2. Activa el reto ecológico para que ya aparezca publicado en el mapa
                const string sqlReto = @"UPDATE retos_ecologicos SET estado = 'Activo' WHERE id_reporte_origen = @idRep;";
                using (var cmdR = new NpgsqlCommand(sqlReto, conn))
                {
                    cmdR.Parameters.AddWithValue("@idRep", id_reporte);
                    cmdR.ExecuteNonQuery();
                }
            }

            return RedirectToAction("Index");
        }

        // NUEVO: Validar un reto directamente desde el Catálogo de Retos
        [HttpPost]
        public IActionResult ValidarRetoDirecto(int id_reto, int id_reporte_origen)
        {
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();

                const string sqlReto = @"UPDATE retos_ecologicos SET estado = 'Activo' WHERE id_reto = @idReto;";
                using (var cmd = new NpgsqlCommand(sqlReto, conn))
                {
                    cmd.Parameters.AddWithValue("@idReto", id_reto);
                    cmd.ExecuteNonQuery();
                }

                if (id_reporte_origen > 0)
                {
                    const string sqlRep = @"UPDATE reportes SET id_estado = 2, fecha_actualizacion = NOW() WHERE id_reporte = @idRep AND id_estado = 1;";
                    using (var cmdR = new NpgsqlCommand(sqlRep, conn))
                    {
                        cmdR.Parameters.AddWithValue("@idRep", id_reporte_origen);
                        cmdR.ExecuteNonQuery();
                    }
                }
            }

            return RedirectToAction("Retos");
        }

        [HttpPost]
        public IActionResult ActualizarEstatusYAsignarPuntos(int id_reporte, string nuevo_estatus, int id_usuario, int puntos, string motivo)
        {
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();

                int idEstado = nuevo_estatus == "Resuelto" ? 3 : (nuevo_estatus == "En Proceso" || nuevo_estatus == "Activo" ? 2 : 1);

                const string sqlUpdate = @"UPDATE reportes SET id_estado = @est, fecha_actualizacion = NOW() WHERE id_reporte = @idRep;";
                using (var cmd = new NpgsqlCommand(sqlUpdate, conn))
                {
                    cmd.Parameters.AddWithValue("@est", idEstado);
                    cmd.Parameters.AddWithValue("@idRep", id_reporte);
                    cmd.ExecuteNonQuery();
                }

                // Sincronizar también el estado del reto asociado
                string nuevoEstadoReto = nuevo_estatus == "Resuelto" ? "Completado" : (idEstado == 2 ? "Activo" : "Por Validar");
                const string sqlSyncReto = @"UPDATE retos_ecologicos SET estado = @estReto WHERE id_reporte_origen = @idRep;";
                using (var cmdR = new NpgsqlCommand(sqlSyncReto, conn))
                {
                    cmdR.Parameters.AddWithValue("@estReto", nuevoEstadoReto);
                    cmdR.Parameters.AddWithValue("@idRep", id_reporte);
                    cmdR.ExecuteNonQuery();
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
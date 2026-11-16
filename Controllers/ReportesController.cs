using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Integradora.Controllers
{
    public class ReportesController : Controller
    {
        private readonly string _connectionString;
        private readonly string _geminiApiKey;
        private readonly IWebHostEnvironment _env;
        private static readonly string[] ExtensionesPermitidas = { ".jpg", ".jpeg", ".png", ".webp" };
        private const long MaxTamanoArchivoBytes = 5 * 1024 * 1024; // 5 MB

        public ReportesController(IWebHostEnvironment env, IConfiguration config)
        {
            _env = env;
            _connectionString = config.GetConnectionString("NeonDb") ?? "";
            _geminiApiKey = config["GeminiApiKey"] ?? "";
        }

        [HttpGet]
        public IActionResult Index()
        {
            string nombre = HttpContext.Session.GetString("UsuarioNombre") ?? "Ciudadano";
            string iniciales = HttpContext.Session.GetString("UsuarioIniciales") ?? "C";
            string rol = HttpContext.Session.GetString("UsuarioRol") ?? "Ciudadano";
            int idUsuario = HttpContext.Session.GetInt32("UsuarioId") ?? 0;

            ViewBag.UsuarioNombre = nombre;
            ViewBag.UsuarioIniciales = iniciales;
            ViewBag.UsuarioRol = rol;

            int misPuntosActuales = 0;
            var listaReportes = new List<dynamic>();
            var listaCategorias = new List<dynamic>();

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    if (idUsuario > 0)
                    {
                        using (var cmdPts = new NpgsqlCommand("SELECT COALESCE(puntos_totales, 0) FROM usuarios WHERE id_usuario = @id;", conn))
                        {
                            cmdPts.Parameters.AddWithValue("@id", idUsuario);
                            var res = cmdPts.ExecuteScalar();
                            if (res != null && res != DBNull.Value) misPuntosActuales = Convert.ToInt32(res);
                        }
                    }

                    using (var cmdCat = new NpgsqlCommand("SELECT id_categoria, nombre FROM categorias_reporte ORDER BY id_categoria ASC;", conn))
                    using (var readerCat = cmdCat.ExecuteReader())
                    {
                        while (readerCat.Read())
                        {
                            listaCategorias.Add(new
                            {
                                Id = Convert.ToInt32(readerCat["id_categoria"]),
                                Nombre = readerCat["nombre"]?.ToString() ?? ""
                            });
                        }
                    }

                    const string sql = @"
                        SELECT r.id_reporte, 
                               r.titulo, 
                               COALESCE(r.descripcion, '') AS descripcion, 
                               r.latitud, 
                               r.longitud, 
                               COALESCE(e.nombre, 'Pendiente') AS estatus,
                               (SELECT url_foto FROM evidencias_fotograficas WHERE id_reporte = r.id_reporte ORDER BY fecha_subida DESC LIMIT 1) AS url_foto,
                               ret.id_reto,
                               COALESCE(ret.puntos_recompensa, 100) AS puntos_reto,
                               COALESCE(ret.titulo, r.titulo) AS titulo_reto
                        FROM reportes r
                        LEFT JOIN estados_reporte e ON r.id_estado = e.id_estado
                        LEFT JOIN retos_ecologicos ret ON ret.id_reporte_origen = r.id_reporte
                        WHERE r.id_estado <> 3
                        ORDER BY r.id_reporte DESC;";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            listaReportes.Add(new
                            {
                                Id = Convert.ToInt32(reader["id_reporte"]),
                                Titulo = reader["titulo"]?.ToString() ?? "Sin título",
                                Descripcion = reader["descripcion"]?.ToString() ?? "",
                                Latitud = Convert.ToDouble(reader["latitud"]),
                                Longitud = Convert.ToDouble(reader["longitud"]),
                                Estatus = reader["estatus"]?.ToString() ?? "Pendiente",
                                Foto = reader["url_foto"]?.ToString() ?? "",
                                IdReto = reader["id_reto"] != DBNull.Value ? Convert.ToInt32(reader["id_reto"]) : Convert.ToInt32(reader["id_reporte"]),
                                PuntosReto = Convert.ToInt32(reader["puntos_reto"]),
                                TituloReto = reader["titulo_reto"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }

            ViewBag.MisPuntosActuales = misPuntosActuales;
            ViewBag.Categorias = listaCategorias;
            ViewBag.Reportes = listaReportes;
            return View();
        }

        [HttpPost]
        public IActionResult GuardarReporte(string titulo, string descripcion, double latitud, double longitud, IFormFile fotoEvidencia, int idCategoria = 1)
        {
            // VALIDACIÓN 2: Verificar sesión activa
            int? idUsuarioSession = HttpContext.Session.GetInt32("UsuarioId");
            if (idUsuarioSession == null || idUsuarioSession <= 0)
            {
                return Json(new { success = false, message = "Tu sesión ha expirado. Inicia sesión nuevamente para reportar." });
            }
            int idUsuario = idUsuarioSession.Value;

            // VALIDACIÓN 4: Título, categoría y rango geográfico válido (Tecámac / Zona Centro)
            if (string.IsNullOrWhiteSpace(titulo) || titulo.Trim().Length < 5)
            {
                return Json(new { success = false, message = "El título de la incidencia debe tener al menos 5 caracteres." });
            }

            if (idCategoria <= 0)
            {
                return Json(new { success = false, message = "Selecciona una categoría ambiental válida." });
            }

            if (latitud < 19.0 || latitud > 20.5 || longitud < -99.5 || longitud > -98.5)
            {
                return Json(new { success = false, message = "Las coordenadas seleccionadas están fuera de la zona de cobertura territorial." });
            }

            string rutaRelativa = null;

            // VALIDACIÓN 3: Formato y tamaño de imagen inicial
            if (fotoEvidencia != null && fotoEvidencia.Length > 0)
            {
                string ext = Path.GetExtension(fotoEvidencia.FileName).ToLowerInvariant();
                if (!ExtensionesPermitidas.Contains(ext))
                {
                    return Json(new { success = false, message = "Formato de imagen no permitido. Usa JPG, PNG o WEBP." });
                }

                if (fotoEvidencia.Length > MaxTamanoArchivoBytes)
                {
                    return Json(new { success = false, message = "La fotografía supera el límite máximo permitido de 5 MB." });
                }

                string carpetaUploads = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
                if (!Directory.Exists(carpetaUploads)) Directory.CreateDirectory(carpetaUploads);

                string nombreArchivo = Guid.NewGuid().ToString() + ext;
                string rutaFisica = Path.Combine(carpetaUploads, nombreArchivo);

                using (var stream = new FileStream(rutaFisica, FileMode.Create))
                {
                    fotoEvidencia.CopyTo(stream);
                }
                rutaRelativa = "/uploads/" + nombreArchivo;
            }

            int idReporteCreado = 0;
            int idRetoCreado = 0;

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        const string sqlRep = @"
                            INSERT INTO reportes (id_usuario, id_categoria, id_estado, titulo, descripcion, latitud, longitud, fecha_creacion, fecha_actualizacion)
                            VALUES (@usr, @cat, 1, @tit, @desc, @lat, @lng, NOW(), NOW()) 
                            RETURNING id_reporte;";

                        using (var cmd = new NpgsqlCommand(sqlRep, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@usr", idUsuario);
                            cmd.Parameters.AddWithValue("@cat", idCategoria);
                            cmd.Parameters.AddWithValue("@tit", titulo.Trim());
                            cmd.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(descripcion) ? (object)DBNull.Value : descripcion.Trim());
                            cmd.Parameters.AddWithValue("@lat", Convert.ToDecimal(latitud));
                            cmd.Parameters.AddWithValue("@lng", Convert.ToDecimal(longitud));
                            idReporteCreado = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        if (!string.IsNullOrEmpty(rutaRelativa) && idReporteCreado > 0)
                        {
                            const string sqlEv = @"
                                INSERT INTO evidencias_fotograficas (id_reporte, url_foto, tipo_evidencia, fecha_subida)
                                VALUES (@rep, @url, 'Reporte Inicial', NOW());";
                            using (var cmdEv = new NpgsqlCommand(sqlEv, conn, trans))
                            {
                                cmdEv.Parameters.AddWithValue("@rep", idReporteCreado);
                                cmdEv.Parameters.AddWithValue("@url", rutaRelativa);
                                cmdEv.ExecuteNonQuery();
                            }
                        }

                        if (idReporteCreado > 0)
                        {
                            const string sqlRetoAuto = @"
                                INSERT INTO retos_ecologicos (id_reporte_origen, titulo, descripcion, fecha_inicio, fecha_fin, puntos_recompensa, estado)
                                VALUES (@rep, @tit, @desc, NOW(), NOW() + INTERVAL '7 days', 100, 'Activo')
                                RETURNING id_reto;";
                            using (var cmdReto = new NpgsqlCommand(sqlRetoAuto, conn, trans))
                            {
                                cmdReto.Parameters.AddWithValue("@rep", idReporteCreado);
                                cmdReto.Parameters.AddWithValue("@tit", "Reto: " + titulo.Trim());
                                cmdReto.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(descripcion) ? "Participa para resolver esta afectación ambiental." : descripcion.Trim());
                                var retResult = cmdReto.ExecuteScalar();
                                if (retResult != null) idRetoCreado = Convert.ToInt32(retResult);
                            }
                        }

                        trans.Commit();
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        return Json(new { success = false, message = "Error al guardar en base de datos: " + ex.Message });
                    }
                }
            }

            return Json(new
            {
                success = true,
                reporte = new
                {
                    Id = idReporteCreado,
                    Titulo = titulo.Trim(),
                    Descripcion = descripcion?.Trim() ?? "",
                    Latitud = latitud,
                    Longitud = longitud,
                    Estatus = "Pendiente",
                    Foto = rutaRelativa ?? "",
                    IdReto = idRetoCreado,
                    PuntosReto = 100,
                    TituloReto = "Reto: " + titulo.Trim()
                }
            });
        }

        [HttpPost]
        public IActionResult UnirseAlReto(int id_reto)
        {
            // VALIDACIÓN 2: Sesión activa
            int? idUsuarioSession = HttpContext.Session.GetInt32("UsuarioId");
            if (idUsuarioSession == null || idUsuarioSession <= 0)
            {
                return RedirectToAction("Index", "Usuarios");
            }

            if (id_reto > 0)
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    const string sqlIns = @"
                        INSERT INTO participantes_retos (id_reto, id_usuario, rol_en_reto, fecha_inscripcion, completo_reto)
                        VALUES (@ret, @usr, 'Voluntario', NOW(), false)
                        ON CONFLICT (id_reto, id_usuario) DO NOTHING;";
                    using (var cmdIns = new NpgsqlCommand(sqlIns, conn))
                    {
                        cmdIns.Parameters.AddWithValue("@ret", id_reto);
                        cmdIns.Parameters.AddWithValue("@usr", idUsuarioSession.Value);
                        cmdIns.ExecuteNonQuery();
                    }
                }
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> CompletarRetoConEvidencia(int id_reto, IFormFile fotoSolucion)
        {
            // VALIDACIÓN 2: Verificar sesión activa
            int? idUsuarioSession = HttpContext.Session.GetInt32("UsuarioId");
            if (idUsuarioSession == null || idUsuarioSession <= 0)
            {
                return Json(new { success = false, message = "Tu sesión ha expirado. Inicia sesión nuevamente." });
            }
            int idUsuario = idUsuarioSession.Value;
            int puntosReto = 100;

            if (id_reto <= 0 || fotoSolucion == null || fotoSolucion.Length == 0)
            {
                return Json(new { success = false, message = "Falta la fotografía de evidencia o datos inválidos." });
            }

            // VALIDACIÓN 3: Formato y tamaño de imagen de solución
            string ext = Path.GetExtension(fotoSolucion.FileName).ToLowerInvariant();
            if (!ExtensionesPermitidas.Contains(ext))
            {
                return Json(new { success = false, message = "Formato no permitido. Sube una fotografía en formato JPG, PNG o WEBP." });
            }

            if (fotoSolucion.Length > MaxTamanoArchivoBytes)
            {
                return Json(new { success = false, message = "La fotografía de evidencia supera el límite máximo de 5 MB." });
            }

            string carpetaUploads = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
            if (!Directory.Exists(carpetaUploads)) Directory.CreateDirectory(carpetaUploads);

            string nombreArchivo = "solucion_" + Guid.NewGuid().ToString() + ext;
            string rutaFisica = Path.Combine(carpetaUploads, nombreArchivo);

            using (var stream = new FileStream(rutaFisica, FileMode.Create))
            {
                await fotoSolucion.CopyToAsync(stream);
            }
            string rutaRelativa = "/uploads/" + nombreArchivo;

            bool esValidoPorIA = false;
            string mensajeValidacion = "No se pudo verificar la imagen con la inteligencia artificial.";

            try
            {
                byte[] imageBytes = System.IO.File.ReadAllBytes(rutaFisica);
                string base64Image = Convert.ToBase64String(imageBytes);
                string mimeType = fotoSolucion.ContentType ?? "image/jpeg";

                string geminiApiKey = _geminiApiKey;
                if (string.IsNullOrEmpty(geminiApiKey))
                {
                    geminiApiKey = Environment.GetEnvironmentVariable("GeminiApiKey") ?? "";
                }
                var url = $"https://generativelanguage.googleapis.com/v1/models/gemini-3.5-flash:generateContent?key={geminiApiKey}";

                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = "Analiza esta fotografía como evidencia de solución de un problema ambiental/urbano. Responde ÚNICAMENTE con un objeto JSON válido, sin bloques de código markdown, con exactamente dos llaves: \"valido\" (booleano true si la imagen muestra una solución clara o trabajo comunitario, false en caso contrario) y \"mensaje\" (string con explicación breve)." },
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = mimeType,
                                        data = base64Image
                                    }
                                }
                            }
                        }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                {
                    var response = await client.PostAsync(url, content);
                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        using (var doc = JsonDocument.Parse(jsonResponse))
                        {
                            var textResult = doc.RootElement
                                .GetProperty("candidates")[0]
                                .GetProperty("content")
                                .GetProperty("parts")[0]
                                .GetProperty("text")
                                .GetString();

                            int inicioJson = textResult.IndexOf('{');
                            int finJson = textResult.LastIndexOf('}');

                            if (inicioJson >= 0 && finJson > inicioJson)
                            {
                                string jsonLimpio = textResult.Substring(inicioJson, finJson - inicioJson + 1);

                                using (var aiResultDoc = JsonDocument.Parse(jsonLimpio))
                                {
                                    if (aiResultDoc.RootElement.TryGetProperty("valido", out var valProp))
                                    {
                                        esValidoPorIA = valProp.GetBoolean();
                                    }
                                    if (aiResultDoc.RootElement.TryGetProperty("mensaje", out var msgProp))
                                    {
                                        mensajeValidacion = msgProp.GetString();
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        return Json(new { success = false, message = "Error de API Gemini: " + jsonResponse });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error de conexión con la IA: " + ex.Message });
            }

            if (!esValidoPorIA)
            {
               
                if (System.IO.File.Exists(rutaFisica)) System.IO.File.Delete(rutaFisica);
                return Json(new { success = false, message = "❌ Evidencia rechazada por la IA: " + mensajeValidacion });
            }

            int idReporteResuelto = 0;

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        using (var cmdReto = new NpgsqlCommand("SELECT estado, puntos_recompensa, id_reporte_origen FROM retos_ecologicos WHERE id_reto = @ret FOR UPDATE;", conn, trans))
                        {
                            cmdReto.Parameters.AddWithValue("@ret", id_reto);
                            using (var reader = cmdReto.ExecuteReader())
                            {
                                if (!reader.Read())
                                {
                                    trans.Rollback();
                                    return Json(new { success = false, message = "El reto especificado no existe." });
                                }

                                if (reader["estado"]?.ToString() == "Completado")
                                {
                                    trans.Rollback();
                                    return Json(new { success = false, message = "Este reto ya fue resuelto y validado anteriormente." });
                                }

                                puntosReto = reader["puntos_recompensa"] != DBNull.Value ? Convert.ToInt32(reader["puntos_recompensa"]) : 100;
                                idReporteResuelto = reader["id_reporte_origen"] != DBNull.Value ? Convert.ToInt32(reader["id_reporte_origen"]) : 0;
                            }
                        }

                        if (idReporteResuelto > 0)
                        {
                            using (var cmdUpdRep = new NpgsqlCommand("UPDATE reportes SET id_estado = 3, fecha_actualizacion = NOW() WHERE id_reporte = @rep;", conn, trans))
                            {
                                cmdUpdRep.Parameters.AddWithValue("@rep", idReporteResuelto);
                                cmdUpdRep.ExecuteNonQuery();
                            }

                            if (!string.IsNullOrEmpty(rutaRelativa))
                            {
                                using (var cmdEv = new NpgsqlCommand("INSERT INTO evidencias_fotograficas (id_reporte, url_foto, tipo_evidencia, fecha_subida) VALUES (@rep, @url, 'Solución Verificada por IA', NOW());", conn, trans))
                                {
                                    cmdEv.Parameters.AddWithValue("@rep", idReporteResuelto);
                                    cmdEv.Parameters.AddWithValue("@url", rutaRelativa);
                                    cmdEv.ExecuteNonQuery();
                                }
                            }
                        }

                        using (var cmdUpdReto = new NpgsqlCommand("UPDATE retos_ecologicos SET estado = 'Completado' WHERE id_reto = @ret;", conn, trans))
                        {
                            cmdUpdReto.Parameters.AddWithValue("@ret", id_reto);
                            cmdUpdReto.ExecuteNonQuery();
                        }

                        using (var cmdUpdPart = new NpgsqlCommand(@"
                            INSERT INTO participantes_retos (id_reto, id_usuario, rol_en_reto, fecha_inscripcion, completo_reto)
                            VALUES (@ret, @usr, 'Solucionador', NOW(), true)
                            ON CONFLICT (id_reto, id_usuario) 
                            DO UPDATE SET rol_en_reto = 'Solucionador', completo_reto = true;", conn, trans))
                        {
                            cmdUpdPart.Parameters.AddWithValue("@ret", id_reto);
                            cmdUpdPart.Parameters.AddWithValue("@usr", idUsuario);
                            cmdUpdPart.ExecuteNonQuery();
                        }

                        using (var cmdSumarPts = new NpgsqlCommand("UPDATE usuarios SET puntos_totales = COALESCE(puntos_totales, 0) + @pts WHERE id_usuario = @usr;", conn, trans))
                        {
                            cmdSumarPts.Parameters.AddWithValue("@pts", puntosReto);
                            cmdSumarPts.Parameters.AddWithValue("@usr", idUsuario);
                            cmdSumarPts.ExecuteNonQuery();
                        }

                        using (var cmdHist = new NpgsqlCommand("INSERT INTO historial_puntos (id_usuario, id_reto, puntos, motivo, fecha) VALUES (@usr, @ret, @pts, 'Reto resuelto y validado por IA', NOW());", conn, trans))
                        {
                            cmdHist.Parameters.AddWithValue("@usr", idUsuario);
                            cmdHist.Parameters.AddWithValue("@ret", id_reto);
                            cmdHist.Parameters.AddWithValue("@pts", puntosReto);
                            cmdHist.ExecuteNonQuery();
                        }

                        trans.Commit();
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        return Json(new { success = false, message = "Error al acreditar puntos en la base de datos: " + ex.Message });
                    }
                }
            }

            return Json(new
            {
                success = true,
                idReporte = idReporteResuelto,
                puntosGanados = puntosReto,
                message = "¡Reto aprobado por IA! Se sumaron +" + puntosReto + " puntos. " + mensajeValidacion
            });
        }
    }
}
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
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
        private const long MaxTamanoArchivoBytes = 5 * 1024 * 1024;

        public ReportesController(IWebHostEnvironment env, IConfiguration config)
        {
            _env = env;
            _connectionString = config.GetConnectionString("NeonDb") ?? "";
            _geminiApiKey = config["GeminiApiKey"] ?? "";
        }

        private async Task<(bool EsValida, string Motivo)> ValidarImagenConGeminiAsync(byte[] imageBytes, string mimeType, string prompt)
        {
            string apiKey = _geminiApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = Environment.GetEnvironmentVariable("GeminiApiKey") ?? "";
            }

            apiKey = apiKey.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (false, "No se encontró la clave de API de Gemini configurada en el servidor.");
            }

            string base64Image = Convert.ToBase64String(imageBytes);
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent?key={apiKey}";
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { text = prompt },
                            new
                            {
                                inlineData = new
                                {
                                    mimeType = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType,
                                    data = base64Image
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    maxOutputTokens = 250,
                    responseMimeType = "application/json"
                }
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            const int maxIntentos = 3;

            for (int intento = 1; intento <= maxIntentos; intento++)
            {
                try
                {
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) })
                    {
                        var response = await client.PostAsync(url, content);
                        string jsonResponse = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            if ((int)response.StatusCode == 503 && intento < maxIntentos)
                            {
                                await Task.Delay(2000);
                                continue;
                            }

                            return (false, $"No se pudo validar la imagen con Gemini 3.8 Flash (HTTP {(int)response.StatusCode}). Detalle: {jsonResponse}");
                        }

                        using (var doc = JsonDocument.Parse(jsonResponse))
                        {
                            var textResult = doc.RootElement
                                .GetProperty("candidates")[0]
                                .GetProperty("content")
                                .GetProperty("parts")[0]
                                .GetProperty("text")
                                .GetString();

                            if (string.IsNullOrWhiteSpace(textResult))
                            {
                                return (false, "La IA no devolvió una respuesta válida. Intenta con otra fotografía.");
                            }

                            int inicioJson = textResult.IndexOf('{');
                            int finJson = textResult.LastIndexOf('}');

                            if (inicioJson >= 0 && finJson > inicioJson)
                            {
                                string jsonLimpio = textResult.Substring(inicioJson, finJson - inicioJson + 1);

                                using (var aiResultDoc = JsonDocument.Parse(jsonLimpio))
                                {
                                    bool valido = aiResultDoc.RootElement.TryGetProperty("valido", out var valProp) && valProp.GetBoolean();
                                    string motivo = aiResultDoc.RootElement.TryGetProperty("mensaje", out var msgProp)
                                        ? (msgProp.GetString() ?? "Validación completada.")
                                        : "Validación completada.";

                                    return (valido, motivo);
                                }
                            }

                            return (false, "No se pudo interpretar el análisis de la IA. Intenta subir la foto nuevamente.");
                        }
                    }
                }
                catch (TaskCanceledException ex)
                {
                    if (intento < maxIntentos)
                    {
                        await Task.Delay(2000);
                        continue;
                    }

                    string detalle = ex.InnerException?.Message ?? ex.Message;
                    return (false, $"Tiempo agotado o conexión cancelada: {detalle}");
                }
                catch (Exception ex)
                {
                    return (false, "Error al conectar con el validador de imágenes: " + ex.Message);
                }
            }

            return (false, "No se pudo completar la validación de la imagen tras varios intentos.");
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
                               COALESCE(e.nombre, 'Activo') AS estatus,
                               (SELECT url_foto FROM evidencias_fotograficas WHERE id_reporte = r.id_reporte ORDER BY id_evidencia ASC LIMIT 1) AS url_foto,
                               ret.id_reto,
                               COALESCE(ret.puntos_recompensa, 100) AS puntos_reto,
                               COALESCE(ret.titulo, r.titulo) AS titulo_reto
                        FROM reportes r
                        LEFT JOIN estados_reporte e ON r.id_estado = e.id_estado
                        INNER JOIN retos_ecologicos ret ON ret.id_reporte_origen = r.id_reporte
                        WHERE r.id_estado = 2 AND LOWER(COALESCE(ret.estado, '')) = 'activo'
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
                                Estatus = reader["estatus"]?.ToString() ?? "Activo",
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
        public async Task<IActionResult> GuardarReporte(string titulo, string descripcion, double latitud, double longitud, IFormFile fotoEvidencia, int idCategoria = 1)
        {
            int? idUsuarioSession = HttpContext.Session.GetInt32("UsuarioId");
            if (idUsuarioSession == null || idUsuarioSession <= 0)
            {
                return Json(new { success = false, message = "Tu sesión ha expirado. Inicia sesión nuevamente para reportar." });
            }
            int idUsuario = idUsuarioSession.Value;

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

            if (fotoEvidencia == null || fotoEvidencia.Length == 0)
            {
                return Json(new { success = false, message = "Debes subir una fotografía obligatoria como evidencia para enviar el reporte a validación." });
            }

            string ext = Path.GetExtension(fotoEvidencia.FileName).ToLowerInvariant();
            if (!ExtensionesPermitidas.Contains(ext))
            {
                return Json(new { success = false, message = "Formato de imagen no permitido. Usa JPG, PNG o WEBP." });
            }

            if (fotoEvidencia.Length > MaxTamanoArchivoBytes)
            {
                return Json(new { success = false, message = "La fotografía supera el límite máximo permitido de 5 MB." });
            }

            string nombreCategoria = "Incidencia Ambiental";
            try
            {
                using (var connCat = new NpgsqlConnection(_connectionString))
                {
                    connCat.Open();
                    using (var cmdCat = new NpgsqlCommand("SELECT nombre FROM categorias_reporte WHERE id_categoria = @cat LIMIT 1;", connCat))
                    {
                        cmdCat.Parameters.AddWithValue("@cat", idCategoria);
                        var resCat = cmdCat.ExecuteScalar();
                        if (resCat != null && resCat != DBNull.Value)
                        {
                            nombreCategoria = resCat.ToString();
                        }
                    }
                }
            }
            catch { }

            byte[] imageBytes;
            using (var ms = new MemoryStream())
            {
                await fotoEvidencia.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }

            string promptReporte = $@"
                Eres el sistema de validación visual de la plataforma ambiental EcoTrack.
                El ciudadano está reportando una incidencia con:
- Categoría seleccionada: '{nombreCategoria}'
- Título del reporte: '{titulo.Trim()}'

                Analiza estrictamente la imagen adjunta y verifica DOS condiciones obligatorias:
1. Que sea una FOTOGRAFÍA REAL tomada en un entorno exterior, urbano o natural (rechaza memes, dibujos, ilustraciones, capturas de pantalla, selfies, fotos de objetos de oficina/casa sin relación o imágenes claramente falsas/generadas por IA).
2. Que el problema visible en la fotografía COINCIDA con la categoría '{nombreCategoria}' (por ejemplo: si la categoría es Fuga de Agua debe verse agua fugándose, tubería rota o escurrimiento; si es Acumulación de Basura debe verse basura o residuos acumulados; si es otra categoría, debe corresponder claramente a esa categoría).

                Responde ÚNICAMENTE con un objeto JSON válido, sin bloques de código markdown, con estas dos llaves exactas:
                {{
                    ""valido"": true o false,
                    ""mensaje"": ""Explicación breve y clara en español para el ciudadano indicando por qué se aprobó o se rechazó la imagen""
                }}";

            var validacionIA = await ValidarImagenConGeminiAsync(imageBytes, fotoEvidencia.ContentType, promptReporte);
            if (!validacionIA.EsValida)
            {
                return Json(new
                {
                    success = false,
                    message = $"❌ Imagen rechazada por la IA ({nombreCategoria}): {validacionIA.Motivo}"
                });
            }

            string carpetaUploads = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
            if (!Directory.Exists(carpetaUploads)) Directory.CreateDirectory(carpetaUploads);

            string nombreArchivo = Guid.NewGuid().ToString() + ext;
            string rutaFisica = Path.Combine(carpetaUploads, nombreArchivo);
            await System.IO.File.WriteAllBytesAsync(rutaFisica, imageBytes);
            string rutaRelativa = "/uploads/" + nombreArchivo;

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
                                VALUES (@rep, @url, 'Reporte Inicial (Validado IA)', NOW());";
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
                                VALUES (@rep, @tit, @desc, NOW(), NOW() + INTERVAL '7 days', 100, 'Por Validar')
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
                        if (System.IO.File.Exists(rutaFisica)) System.IO.File.Delete(rutaFisica);
                        return Json(new { success = false, message = "Error al guardar en base de datos: " + ex.Message });
                    }
                }
            }

            return Json(new
            {
                success = true,
                requiereValidacion = true,
                message = $"✅ Imagen aprobada por IA ({validacionIA.Motivo}). Tu reporte fue enviado a validación del Administrador.",
                reporte = new
                {
                    Id = idReporteCreado,
                    Titulo = titulo.Trim(),
                    Descripcion = descripcion?.Trim() ?? "",
                    Latitud = latitud,
                    Longitud = longitud,
                    Estatus = "Por Validar",
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
            int? idUsuarioSession = HttpContext.Session.GetInt32("UsuarioId");
            if (idUsuarioSession == null || idUsuarioSession <= 0)
            {
                return Json(new { success = false, message = "Tu sesión ha expirado. Inicia sesión nuevamente." });
            }
            int idUsuario = idUsuarioSession.Value;
            int puntosReto = 100;

            if (id_reto <= 0 || fotoSolucion == null || fotoSolucion.Length == 0)
            {
                return Json(new { success = false, message = "Debes subir una fotografía de evidencia para completar el reto." });
            }

            string ext = Path.GetExtension(fotoSolucion.FileName).ToLowerInvariant();
            if (!ExtensionesPermitidas.Contains(ext))
            {
                return Json(new { success = false, message = "Formato no permitido. Sube una fotografía en formato JPG, PNG o WEBP." });
            }

            if (fotoSolucion.Length > MaxTamanoArchivoBytes)
            {
                return Json(new { success = false, message = "La fotografía de evidencia supera el límite máximo de 5 MB." });
            }

            string nombreCategoria = "Incidencia Ambiental";
            string tituloReto = "Reto Ecológico";
            try
            {
                using (var connInfo = new NpgsqlConnection(_connectionString))
                {
                    connInfo.Open();
                    const string sqlInfo = @"
                        SELECT ret.titulo, COALESCE(c.nombre, 'Incidencia Ambiental') AS categoria
                        FROM retos_ecologicos ret
                        LEFT JOIN reportes r ON ret.id_reporte_origen = r.id_reporte
                        LEFT JOIN categorias_reporte c ON r.id_categoria = c.id_categoria
                        WHERE ret.id_reto = @ret LIMIT 1;";
                    using (var cmdInfo = new NpgsqlCommand(sqlInfo, connInfo))
                    {
                        cmdInfo.Parameters.AddWithValue("@ret", id_reto);
                        using (var reader = cmdInfo.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                tituloReto = reader["titulo"]?.ToString() ?? tituloReto;
                                nombreCategoria = reader["categoria"]?.ToString() ?? nombreCategoria;
                            }
                        }
                    }
                }
            }
            catch { }

            byte[] imageBytes;
            using (var ms = new MemoryStream())
            {
                await fotoSolucion.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }

            string promptSolucion = $@"
                Eres el validador de soluciones ambientales de EcoTrack.
                El ciudadano afirma haber resuelto el reto '{tituloReto}' de la categoría '{nombreCategoria}'.
                Analiza la fotografía y verifica:
1. Que sea una fotografía real de un espacio exterior, calle, tubería, área verde o zona urbana (rechaza memes, capturas de pantalla, dibujos o fotos irrelevantes).
2. Que muestre evidencia razonable de que el problema de '{nombreCategoria}' fue atendido o limpiado (por ejemplo: calle limpia sin basura, tubería o pavimento reparado sin fuga de agua, área verde cuidada, etc.).

                Responde ÚNICAMENTE con un objeto JSON válido, sin markdown, con estas dos llaves:
                {{
                    ""valido"": true o false,
                    ""mensaje"": ""Explicación breve en español""
                }}";

            var validacionIA = await ValidarImagenConGeminiAsync(imageBytes, fotoSolucion.ContentType, promptSolucion);
            if (!validacionIA.EsValida)
            {
                return Json(new { success = false, message = "❌ Evidencia rechazada por la IA: " + validacionIA.Motivo });
            }

            string carpetaUploads = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
            if (!Directory.Exists(carpetaUploads)) Directory.CreateDirectory(carpetaUploads);

            string nombreArchivo = "solucion_" + Guid.NewGuid().ToString() + ext;
            string rutaFisica = Path.Combine(carpetaUploads, nombreArchivo);
            await System.IO.File.WriteAllBytesAsync(rutaFisica, imageBytes);
            string rutaRelativa = "/uploads/" + nombreArchivo;

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
                        if (System.IO.File.Exists(rutaFisica)) System.IO.File.Delete(rutaFisica);
                        return Json(new { success = false, message = "Error al acreditar puntos en la base de datos: " + ex.Message });
                    }
                }
            }

            return Json(new
            {
                success = true,
                idReporte = idReporteResuelto,
                puntosGanados = puntosReto,
                message = "¡Reto aprobado! Se sumaron +" + puntosReto + " puntos. " + validacionIA.Motivo
            });
        }
    }
}
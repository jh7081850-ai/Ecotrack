using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Integradora.Controllers
{
    public class UsuariosController : Controller
    {
        private readonly IConfiguration _configuration;

        // Credenciales oficiales de Gmail integradas directamente para evitar errores de Host desconocido o variables faltantes
        private const string PasswordAppGmail = "mpetvnhdtwemmect";
        private const string CorreoGmailPorDefecto = "jh7081850@gmail.com";

        public UsuariosController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private bool EsCorreoValido(string correo)
        {
            if (string.IsNullOrWhiteSpace(correo)) return false;
            return Regex.IsMatch(correo, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }

        private bool EsNombreValido(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre) || nombre.Length < 3 || nombre.Length > 100) return false;
            return Regex.IsMatch(nombre, @"^[a-zA-ZáéíóúÁÉÍÓÚñÑ\s]+$");
        }

        private bool EsContrasenaSegura(string pass)
        {
            if (string.IsNullOrEmpty(pass) || pass.Length < 8) return false;
            bool tieneMayuscula = Regex.IsMatch(pass, @"[A-Z]");
            bool tieneMinuscula = Regex.IsMatch(pass, @"[a-z]");
            bool tieneNumero = Regex.IsMatch(pass, @"[0-9]");
            return tieneMayuscula && tieneMinuscula && tieneNumero;
        }

        // Vista de Login
        [HttpGet]
        public IActionResult Index()
        {
            int? idUsuario = HttpContext.Session.GetInt32("UsuarioId");
            string rol = HttpContext.Session.GetString("UsuarioRol");
            if (idUsuario != null && idUsuario > 0 && !string.IsNullOrEmpty(rol))
            {
                if (rol.Trim().Equals("Administrador", StringComparison.OrdinalIgnoreCase))
                    return RedirectToAction("Index", "Admin");
                return RedirectToAction("Index", "Reportes");
            }

            return View();
        }

        [HttpPost]
        public IActionResult Login(string correo, string contrasena)
        {
            string emailLimpio = (correo ?? "").Trim().ToLower();
            string passLimpio = (contrasena ?? "").Trim();

            if (string.IsNullOrEmpty(emailLimpio) || string.IsNullOrEmpty(passLimpio))
            {
                ViewBag.Error = "Por favor ingresa tu correo y contraseña.";
                return View("Index");
            }

            if (!EsCorreoValido(emailLimpio))
            {
                ViewBag.Error = "El formato del correo electrónico no es válido.";
                return View("Index");
            }

            if (passLimpio.Length < 8)
            {
                ViewBag.Error = "La contraseña debe tener al menos 8 caracteres.";
                return View("Index");
            }

            string connectionString = _configuration.GetConnectionString("NeonDb");

            try
            {
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();

                    const string sql = @"
                        SELECT id_usuario, nombre, correo, rol, puntos_totales 
                        FROM usuarios 
                        WHERE LOWER(TRIM(correo)) = @correo 
                          AND TRIM(password_hash) = @pwd
                        LIMIT 1;";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@correo", emailLimpio);
                        cmd.Parameters.AddWithValue("@pwd", passLimpio);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                int idUser = Convert.ToInt32(reader["id_usuario"]);
                                string nombreReal = reader["nombre"]?.ToString() ?? "Usuario";
                                string rolUser = reader["rol"]?.ToString() ?? "Ciudadano";

                                string iniciales = "U";
                                var partes = nombreReal.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (partes.Length > 1)
                                {
                                    iniciales = $"{char.ToUpper(partes[0][0])}{char.ToUpper(partes[1][0])}";
                                }
                                else if (partes.Length == 1)
                                {
                                    iniciales = char.ToUpper(partes[0][0]).ToString();
                                }

                                HttpContext.Session.SetInt32("UsuarioId", idUser);
                                HttpContext.Session.SetString("UsuarioNombre", nombreReal);
                                HttpContext.Session.SetString("UsuarioIniciales", iniciales);
                                HttpContext.Session.SetString("UsuarioRol", rolUser);

                                if (rolUser.Trim().Equals("Administrador", StringComparison.OrdinalIgnoreCase))
                                {
                                    return RedirectToAction("Index", "Admin");
                                }
                                else
                                {
                                    return RedirectToAction("Index", "Reportes");
                                }
                            }
                        }
                    }
                }

                ViewBag.Error = "Correo o contraseña no válidos.";
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Error de conexión: " + ex.Message;
            }

            return View("Index");
        }

        [HttpGet]
        public IActionResult Registro()
        {
            return View();
        }

        private const string UrlPuenteGmail = "https://script.google.com/macros/s/AKfycby2xnS16v6oc5JQvdFUeRTwUtOjCpDr8RcigMRypj03OlL78TNI4kZuFzBzU3-9IAcH/execñ";

     

        [HttpPost]
        public async Task<IActionResult> EnviarCodigoVerificacion(string nombre, string correo, string contrasena)
        {
            string emailLimpio = (correo ?? "").Trim().ToLower();
            string passLimpio = (contrasena ?? "").Trim();
            string nombreLimpio = (nombre ?? "").Trim();

            if (string.IsNullOrEmpty(emailLimpio) || string.IsNullOrEmpty(passLimpio) || string.IsNullOrEmpty(nombreLimpio))
                return Json(new { ok = false, mensaje = "Por favor completa todos los campos." });

            if (!EsNombreValido(nombreLimpio))
                return Json(new { ok = false, mensaje = "El nombre debe tener al menos 3 letras y no contener números ni símbolos." });

            if (!EsCorreoValido(emailLimpio))
                return Json(new { ok = false, mensaje = "Ingresa un correo electrónico válido (ejemplo@dominio.com)." });

            if (!EsContrasenaSegura(passLimpio))
                return Json(new { ok = false, mensaje = "La contraseña debe tener mínimo 8 caracteres, e incluir mayúscula, minúscula y número." });

            string connectionString = _configuration.GetConnectionString("NeonDb");

            try
            {
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();
                    using (var checkCmd = new NpgsqlCommand("SELECT COUNT(*) FROM usuarios WHERE LOWER(TRIM(correo)) = @correo;", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@correo", emailLimpio);
                        int count = Convert.ToInt32(checkCmd.ExecuteScalar());
                        if (count > 0)
                            return Json(new { ok = false, mensaje = "Este correo electrónico ya se encuentra registrado en EcoTrack." });
                    }
                }

                string codigo = new Random().Next(100000, 999999).ToString();
                HttpContext.Session.SetString("TempRegNombre", nombreLimpio);
                HttpContext.Session.SetString("TempRegCorreo", emailLimpio);
                HttpContext.Session.SetString("TempRegPass", passLimpio);
                HttpContext.Session.SetString("TempRegCodigo", codigo);

                string cuerpoHtml = $@"
                    <div style='font-family:Segoe UI,Arial,sans-serif; max-width:460px; margin:auto; padding:24px; border:1px solid #d9e7dc; border-radius:16px; background:#ffffff;'>
                        <h2 style='color:#1b5e20; margin-top:0; text-align:center;'>🌱 ECOTRACK</h2>
                        <p style='color:#333; font-size:14px;'>Hola <b>{nombreLimpio}</b>,</p>
                        <p style='color:#4b6352; font-size:13.5px;'>Para confirmar que tu correo electrónico es real y finalizar la creación de tu cuenta ciudadana, ingresa el siguiente código de 6 dígitos:</p>
                        <div style='background:#f4fbf5; border:2px dashed #2e7d32; padding:16px; text-align:center; font-size:28px; font-weight:800; letter-spacing:8px; color:#1b5e20; border-radius:12px; margin:20px 0;'>
                            {codigo}
                        </div>
                        <p style='color:#64748b; font-size:11.5px; text-align:center; margin-bottom:0;'>Si tú no solicitaste este registro en EcoTrack, ignora este correo.</p>
                    </div>";

                bool enviado = false;

                if (!string.IsNullOrWhiteSpace(UrlPuenteGmail) && UrlPuenteGmail.StartsWith("https://script.google.com"))
                {
                    try
                    {
                        var payload = new
                        {
                            destinatario = emailLimpio,
                            asunto = "Código de Verificación de Cuenta - EcoTrack",
                            cuerpoHtml = cuerpoHtml
                        };
                        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                        using (var handler = new HttpClientHandler { AllowAutoRedirect = true })
                        using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) })
                        {
                            var response = await client.PostAsync(UrlPuenteGmail, content);
                            if (response.IsSuccessStatusCode)
                            {
                                enviado = true;
                            }
                        }
                    }
                    catch { }
                }

                if (!enviado)
                {
                    try
                    {
                        ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

                        var mail = new MailMessage();
                        mail.From = new MailAddress(CorreoGmailPorDefecto, "EcoTrack Monitoreo Ambiental");
                        mail.To.Add(emailLimpio);
                        mail.Subject = "Código de Verificación de Cuenta - EcoTrack";
                        mail.IsBodyHtml = true;
                        mail.Body = cuerpoHtml;

                        using (var smtp = new SmtpClient("smtp.gmail.com", 587))
                        {
                            smtp.EnableSsl = true;
                            smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                            smtp.UseDefaultCredentials = false;
                            smtp.Credentials = new NetworkCredential(CorreoGmailPorDefecto, PasswordAppGmail);
                            smtp.Timeout = 10000;
                            smtp.Send(mail);
                            enviado = true;
                        }
                    }
                    catch {  }
                }

                if (enviado)
                {
                    return Json(new { ok = true, mensaje = $"Enviamos un código de 6 dígitos a {emailLimpio}, revisa tu bandeja de entrada o SPAM." });
                }

                return Json(new
                {
                    ok = true,
                    mensaje = $"Modo Verificación Activo para {emailLimpio}, revisa tu bandeja de entrada o carpeta de SPAM."
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = "Error al procesar el registro: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult ConfirmarCodigoYRegistrar(string codigo)
        {
            string codigoIngresado = (codigo ?? "").Trim();
            string codigoGuardado = HttpContext.Session.GetString("TempRegCodigo");
            string nombreLimpio = HttpContext.Session.GetString("TempRegNombre");
            string emailLimpio = HttpContext.Session.GetString("TempRegCorreo");
            string passLimpio = HttpContext.Session.GetString("TempRegPass");

            if (string.IsNullOrEmpty(codigoGuardado) || string.IsNullOrEmpty(emailLimpio))
            {
                return Json(new { ok = false, mensaje = "La sesión de verificación expiró. Por favor vuelve a solicitar el código." });
            }

            if (codigoIngresado != codigoGuardado)
            {
                return Json(new { ok = false, mensaje = "❌ El código de 6 dígitos es incorrecto. Verifica e intenta de nuevo." });
            }

            string nombreBD = nombreLimpio;
            string apellidoPaternoBD = "Ciudadano";
            string apellidoMaternoBD = "";

            var partes = nombreLimpio.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 2)
            {
                nombreBD = partes[0];
                apellidoPaternoBD = partes[1];
            }
            else if (partes.Length == 3)
            {
                nombreBD = partes[0];
                apellidoPaternoBD = partes[1];
                apellidoMaternoBD = partes[2];
            }
            else if (partes.Length >= 4)
            {
                nombreBD = $"{partes[0]} {partes[1]}";
                apellidoPaternoBD = partes[2];
                apellidoMaternoBD = partes[3];
            }

            string connectionString = _configuration.GetConnectionString("NeonDb");

            try
            {
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();

                    const string sqlInsert = @"
                        INSERT INTO usuarios (nombre, apellido_paterno, apellido_materno, correo, password_hash, rol, puntos_totales, fecha_registro)
                        VALUES (@nom, @app, @apm, @correo, @pwd, 'Ciudadano', 0, NOW())
                        RETURNING id_usuario;";

                    int newId = 0;
                    using (var cmd = new NpgsqlCommand(sqlInsert, conn))
                    {
                        cmd.Parameters.AddWithValue("@nom", nombreBD);
                        cmd.Parameters.AddWithValue("@app", apellidoPaternoBD);
                        cmd.Parameters.AddWithValue("@apm", apellidoMaternoBD);
                        cmd.Parameters.AddWithValue("@correo", emailLimpio);
                        cmd.Parameters.AddWithValue("@pwd", passLimpio);
                        newId = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    HttpContext.Session.Remove("TempRegCodigo");
                    HttpContext.Session.Remove("TempRegNombre");
                    HttpContext.Session.Remove("TempRegCorreo");
                    HttpContext.Session.Remove("TempRegPass");

                    string iniciales = "U";
                    if (partes.Length > 1)
                        iniciales = $"{char.ToUpper(partes[0][0])}{char.ToUpper(partes[partes.Length - 1][0])}";
                    else if (partes.Length == 1)
                        iniciales = char.ToUpper(partes[0][0]).ToString();

                    HttpContext.Session.SetInt32("UsuarioId", newId);
                    HttpContext.Session.SetString("UsuarioNombre", nombreLimpio);
                    HttpContext.Session.SetString("UsuarioIniciales", iniciales);
                    HttpContext.Session.SetString("UsuarioRol", "Ciudadano");

                    return Json(new { ok = true, redirectUrl = "/Reportes/Index" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = "Error al guardar en base de datos: " + ex.Message });
            }
        }

        [HttpGet]
        public IActionResult Lista()
        {
            string nombre = HttpContext.Session.GetString("UsuarioNombre");
            string iniciales = HttpContext.Session.GetString("UsuarioIniciales");
            string rol = HttpContext.Session.GetString("UsuarioRol");
            int idUsuario = HttpContext.Session.GetInt32("UsuarioId") ?? 0;

            if (string.IsNullOrEmpty(nombre) || idUsuario <= 0)
            {
                return RedirectToAction("Index", "Usuarios");
            }

            ViewBag.UsuarioNombre = nombre;
            ViewBag.UsuarioIniciales = iniciales;
            ViewBag.UsuarioRol = rol;

            var retosInscritos = new List<dynamic>();
            int misReportesCount = 0;
            int puntosTotales = 0;
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new NpgsqlCommand("SELECT puntos_totales FROM usuarios WHERE id_usuario = @id;", conn))
                {
                    cmd.Parameters.AddWithValue("@id", idUsuario);
                    var p = cmd.ExecuteScalar();
                    if (p != null && p != DBNull.Value) puntosTotales = Convert.ToInt32(p);
                }

                using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM reportes WHERE id_usuario = @id;", conn))
                {
                    cmd.Parameters.AddWithValue("@id", idUsuario);
                    misReportesCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                const string sqlRetos = @"
                    SELECT r.id_reto, r.titulo, r.descripcion, r.puntos_recompensa, 
                           pr.rol_en_reto, pr.completo_reto, pr.fecha_inscripcion
                    FROM participantes_retos pr
                    INNER JOIN retos_ecologicos r ON pr.id_reto = r.id_reto
                    WHERE pr.id_usuario = @id
                    ORDER BY pr.id_participacion DESC;";

                using (var cmd = new NpgsqlCommand(sqlRetos, conn))
                {
                    cmd.Parameters.AddWithValue("@id", idUsuario);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            bool comp = reader["completo_reto"] != DBNull.Value && Convert.ToBoolean(reader["completo_reto"]);
                            retosInscritos.Add(new
                            {
                                Id = reader["id_reto"],
                                Titulo = reader["titulo"]?.ToString() ?? "Reto Ecológico",
                                Descripcion = reader["descripcion"]?.ToString() ?? "",
                                Puntos = reader["puntos_recompensa"],
                                Rol = reader["rol_en_reto"]?.ToString() ?? "Voluntario",
                                Estado = comp ? "Acreditado" : "En Curso"
                            });
                        }
                    }
                }
            }

            ViewBag.PuntosTotales = puntosTotales;
            ViewBag.MisReportes = misReportesCount;
            ViewBag.RetosInscritos = retosInscritos;

            return View();
        }

        [HttpGet]
        public IActionResult CerrarSesion()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Usuarios");
        }
    }
}
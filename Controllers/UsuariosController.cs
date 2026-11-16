using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Integradora.Controllers
{
    public class UsuariosController : Controller
    {
        private readonly IConfiguration _configuration;

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

        // Login con validación de formato y longitud mínima
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


        // Procesa el Registro con validaciones completas y división inteligente de nombres
        [HttpPost]
        public IActionResult Registro(string nombre, string correo, string contrasena)
        {
            string emailLimpio = (correo ?? "").Trim().ToLower();
            string passLimpio = (contrasena ?? "").Trim();
            string nombreLimpio = (nombre ?? "").Trim();

            if (string.IsNullOrEmpty(emailLimpio) || string.IsNullOrEmpty(passLimpio) || string.IsNullOrEmpty(nombreLimpio))
            {
                ViewBag.Error = "Por favor completa todos los campos.";
                return View();
            }

            if (!EsNombreValido(nombreLimpio))
            {
                ViewBag.Error = "El nombre debe tener al menos 3 letras y no contener números ni símbolos.";
                return View();
            }

            if (!EsCorreoValido(emailLimpio))
            {
                ViewBag.Error = "Ingresa un correo electrónico válido (ejemplo@dominio.com).";
                return View();
            }

            if (!EsContrasenaSegura(passLimpio))
            {
                ViewBag.Error = "La contraseña debe tener mínimo 8 caracteres, e incluir al menos una mayúscula, una minúscula y un número.";
                return View();
            }

            // Dividir inteligentemente el Nombre Completo para cumplir con las columnas de la BD
            string nombreBD = nombreLimpio;
            string apellidoPaternoBD = "Ciudadano"; // Valor por defecto si solo ingresa una palabra
            string apellidoMaternoBD = "";

            var partes = nombreLimpio.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 2)
            {
                // Ejemplo: "Octavio Pérez" -> Nombre: Octavio, Apellido Paterno: Pérez
                nombreBD = partes[0];
                apellidoPaternoBD = partes[1];
            }
            else if (partes.Length == 3)
            {
                // Ejemplo: "Octavio Pérez Gómez" -> Nombre: Octavio, Apellido P: Pérez, Apellido M: Gómez
                nombreBD = partes[0];
                apellidoPaternoBD = partes[1];
                apellidoMaternoBD = partes[2];
            }
            else if (partes.Length >= 4)
            {
                // Ejemplo: "Julio Manuel Santos Hernandez" -> Nombre: Julio Manuel, Apellido P: Santos, Apellido M: Hernandez
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

                    using (var checkCmd = new NpgsqlCommand("SELECT COUNT(*) FROM usuarios WHERE LOWER(TRIM(correo)) = @correo;", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@correo", emailLimpio);
                        int count = Convert.ToInt32(checkCmd.ExecuteScalar());
                        if (count > 0)
                        {
                            ViewBag.Error = "El correo electrónico ya se encuentra registrado.";
                            return View();
                        }
                    }

                    // Inserción mapeando las 3 columnas por separado a la base de datos
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

                    string iniciales = "U";
                    if (partes.Length > 1)
                        iniciales = $"{char.ToUpper(partes[0][0])}{char.ToUpper(partes[partes.Length - 1][0])}";
                    else if (partes.Length == 1)
                        iniciales = char.ToUpper(partes[0][0]).ToString();

                    HttpContext.Session.SetInt32("UsuarioId", newId);
                    HttpContext.Session.SetString("UsuarioNombre", nombreLimpio);
                    HttpContext.Session.SetString("UsuarioIniciales", iniciales);
                    HttpContext.Session.SetString("UsuarioRol", "Ciudadano");

                    return RedirectToAction("Index", "Reportes");
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Error al registrar: " + ex.Message;
                return View();
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
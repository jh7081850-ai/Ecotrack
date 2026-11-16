using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;

namespace Integradora.Controllers
{
    public class ParticipantesRetosController : Controller
    {
        private readonly IConfiguration _configuration;

        // Inyectamos IConfiguration para obtener la conexión de forma segura
        public ParticipantesRetosController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Index(string nombre = "Administrador", string iniciales = "AD", string rol = "Administrador")
        {
            ViewBag.UsuarioNombre = nombre;
            ViewBag.UsuarioIniciales = iniciales;
            ViewBag.UsuarioRol = rol;

            var inscripciones = new List<dynamic>();

            // Obtenemos la cadena de conexión configurada (NeonDb)
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                const string sql = @"SELECT pr.id_participacion, 
                                            (u.nombre || ' ' || u.apellido_paterno) AS usuario, 
                                            r.titulo AS nombre_reto, 
                                            pr.rol_en_reto,
                                            pr.completo_reto, 
                                            pr.fecha_inscripcion 
                                     FROM participantes_retos pr
                                     INNER JOIN usuarios u ON pr.id_usuario = u.id_usuario
                                     INNER JOIN retos_ecologicos r ON pr.id_reto = r.id_reto
                                     ORDER BY pr.id_participacion DESC;";

                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        bool completado = reader["completo_reto"] != DBNull.Value && Convert.ToBoolean(reader["completo_reto"]);

                        inscripciones.Add(new
                        {
                            Id = reader["id_participacion"],
                            Usuario = reader["usuario"]?.ToString() ?? "Sin nombre",
                            Reto = reader["nombre_reto"]?.ToString() ?? "Sin título",
                            RolEnReto = reader["rol_en_reto"]?.ToString() ?? "Voluntario",
                            Completo = completado,
                            Estatus = completado ? "Completado" : "En Progreso",
                            Fecha = reader["fecha_inscripcion"] != DBNull.Value
                                    ? Convert.ToDateTime(reader["fecha_inscripcion"]).ToString("dd/MM/yyyy HH:mm")
                                    : "N/A"
                        });
                    }
                }
            }

            ViewBag.Inscripciones = inscripciones;
            return View();
        }
    }
}
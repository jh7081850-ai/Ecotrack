using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;

namespace Integradora.Controllers
{
    public class RetosEcologicosController : Controller
    {
        private readonly IConfiguration _configuration;

        public RetosEcologicosController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Index(string nombre = "Administrador", string iniciales = "AD", string rol = "Administrador")
        {
            ViewBag.UsuarioNombre = nombre;
            ViewBag.UsuarioIniciales = iniciales;
            ViewBag.UsuarioRol = rol;

            var retos = new List<dynamic>();
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                const string sql = "SELECT id_reto, titulo, descripcion, puntos_recompensa, fecha_inicio, fecha_fin, estado FROM retos_ecologicos ORDER BY id_reto DESC;";

                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        retos.Add(new
                        {
                            Id = reader["id_reto"],
                            Titulo = reader["titulo"].ToString(),
                            Descripcion = reader["descripcion"].ToString(),
                            Puntos = reader["puntos_recompensa"],
                            Inicio = Convert.ToDateTime(reader["fecha_inicio"]).ToString("dd/MM/yyyy"),
                            Fin = Convert.ToDateTime(reader["fecha_fin"]).ToString("dd/MM/yyyy"),
                            Estado = reader["estado"]?.ToString() ?? "Activo"
                        });
                    }
                }
            }

            ViewBag.Retos = retos;
            return View();
        }

        [HttpPost]
        public IActionResult Crear(string titulo, string descripcion, int puntos, DateTime fecha_inicio, DateTime fecha_fin)
        {
            string connectionString = _configuration.GetConnectionString("NeonDb");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                const string sql = @"INSERT INTO retos_ecologicos (titulo, descripcion, puntos_recompensa, fecha_inicio, fecha_fin, estado) 
                            VALUES (@tit, @desc, @pts, @ini, @fin, 'Activo');";
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@tit", titulo ?? "");
                    cmd.Parameters.AddWithValue("@desc", descripcion ?? "");
                    cmd.Parameters.AddWithValue("@pts", puntos);
                    cmd.Parameters.AddWithValue("@ini", fecha_inicio);
                    cmd.Parameters.AddWithValue("@fin", fecha_fin);
                    cmd.ExecuteNonQuery();
                }
            }
            return RedirectToAction("Index");
        }
    }
}
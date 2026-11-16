using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Integradora.Controllers
{
    public class HistorialPuntosController : Controller
    {
        private readonly IConfiguration _configuration;
        public HistorialPuntosController(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        [HttpGet]
        public IActionResult Index(string nombre = "Administrador", string iniciales = "AD", string rol = "Administrador")
        {
            ViewBag.UsuarioNombre = nombre;
            ViewBag.UsuarioIniciales = iniciales;
            ViewBag.UsuarioRol = rol;
            string connectionString = _configuration.GetConnectionString("NeonDb");

            var movimientos = new List<dynamic>();

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                const string sql = @"SELECT hp.id_transaccion, 
                                            (u.nombre || ' ' || u.apellido_paterno) AS usuario, 
                                            hp.puntos, 
                                            hp.motivo, 
                                            hp.fecha 
                                     FROM historial_puntos hp
                                     INNER JOIN usuarios u ON hp.id_usuario = u.id_usuario
                                     ORDER BY hp.id_transaccion DESC;";

                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        movimientos.Add(new
                        {
                            Id = reader["id_transaccion"],
                            Usuario = reader["usuario"]?.ToString() ?? "Sin nombre",
                            Puntos = reader["puntos"],
                            Motivo = reader["motivo"]?.ToString() ?? "Actividad ecológica",
                            Fecha = reader["fecha"] != DBNull.Value
                                    ? Convert.ToDateTime(reader["fecha"]).ToString("dd/MM/yyyy HH:mm")
                                    : "N/A"
                        });
                    }
                }
            }

            ViewBag.Movimientos = movimientos;
            return View();
        }
    }
}
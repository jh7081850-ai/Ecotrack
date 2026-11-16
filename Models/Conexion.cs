using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;

namespace Integradora.Models
{
    public class Conexion
    {
        private readonly IConfiguration? _configuration;
        private static IConfiguration? _staticConfiguration;
        private NpgsqlConnection _connection = new();

        // Constructor con inyección de dependencias
        public Conexion(IConfiguration configuration)
        {
            _configuration = configuration;
            _staticConfiguration = configuration;
        }

        // Constructor sin parámetros (para que los `new Conexion()` existentes no fallen)
        public Conexion() { }

        private string GetConnectionString()
        {
            // 1. Intenta obtenerlo desde la configuración inyectada
            if (_configuration != null)
            {
                var conn = _configuration.GetConnectionString("NeonDb");
                if (!string.IsNullOrEmpty(conn)) return conn;
            }

            // 2. Intenta obtenerlo desde la configuración estática
            if (_staticConfiguration != null)
            {
                var conn = _staticConfiguration.GetConnectionString("NeonDb");
                if (!string.IsNullOrEmpty(conn)) return conn;
            }

            // 3. Fallback a la variable de entorno de Render / Sistema operativo
            var envConn = Environment.GetEnvironmentVariable("ConnectionStrings__NeonDb")
                          ?? Environment.GetEnvironmentVariable("NeonDb");
            if (!string.IsNullOrEmpty(envConn)) return envConn;

            return "";
        }

        public DataTable GetQuery(string sql)
        {
            DataTable tb = new();
            NpgsqlDataAdapter adapter = new();
            using (_connection = new(GetConnectionString()))
            {
                using (var command = new NpgsqlCommand())
                {
                    _connection.Open();
                    command.Connection = _connection;
                    command.CommandText = sql;
                    adapter.SelectCommand = command;
                    adapter.Fill(tb);
                }
            }
            return tb;
        }

        public int ExecuteQuery(string sql, List<NpgsqlParameter> parameters)
        {
            try
            {
                using (_connection = new(GetConnectionString()))
                {
                    using (NpgsqlCommand command = new NpgsqlCommand())
                    {
                        _connection.Open();
                        command.Connection = _connection;
                        command.CommandText = sql;
                        command.Parameters.Clear();
                        if (parameters != null)
                        {
                            foreach (NpgsqlParameter param in parameters)
                            {
                                command.Parameters.Add(param);
                            }
                        }
                        return command.ExecuteNonQuery();
                    }
                }
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return -1; // Duplicado
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public DataTable GetQuery(string sql, List<NpgsqlParameter> parameters)
        {
            DataTable tabla = new();
            NpgsqlDataAdapter adapter = new();
            using (_connection = new(GetConnectionString()))
            {
                using (NpgsqlCommand command = new NpgsqlCommand())
                {
                    _connection.Open();
                    command.Connection = _connection;
                    command.CommandText = sql;
                    command.Parameters.Clear();
                    if (parameters != null)
                    {
                        foreach (NpgsqlParameter param in parameters)
                        {
                            command.Parameters.Add(param);
                        }
                    }
                    adapter.SelectCommand = command;
                    adapter.Fill(tabla);
                }
                return tabla;
            }
        }
    }
}
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;

namespace Integradora.Models
{
    public class Conexion
    {
        private readonly IConfiguration _configuration;
        private NpgsqlConnection _connection = new();

        // Inyectamos IConfiguration para obtener la cadena desde appsettings o Render
        public Conexion(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string GetConnectionString()
        {
            return _configuration.GetConnectionString("NeonDb") ?? "";
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
                return -1; // Duplicado / Violación de índice único
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
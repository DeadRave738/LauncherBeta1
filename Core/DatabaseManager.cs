using System;
using System.Collections.Generic;
using System.Data;
using MySqlConnector;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Core
{
    private readonly MySqlConnection _connection;
    // Добавьте этот метод в класс DatabaseManager
    private MySqlConnection GetConnection()
    {
        string connectionString = ConfigManager.GetConnectionString();
        return new MySqlConnection(connectionString);
    }

    // Добавьте этот метод в класс DatabaseManager
    public List<User> GetUsers()
    {
        var users = new List<User>();

        using (var connection = GetConnection())
        {
            try
            {
                connection.Open();
                string query = "SELECT * FROM users";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        users.Add(new User
                        {
                            Id = reader.GetInt32("id"),
                            Username = reader.GetString("username"),
                            AccessToken = reader.IsDBNull(reader.GetOrdinal("access_token")) ? null : reader.GetString("access_token"),
                            ClientToken = reader.IsDBNull(reader.GetOrdinal("client_token")) ? null : reader.GetString("client_token")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении пользователей: {ex.Message}");
                throw;
            }
        }

        return users;
    }
    public class DatabaseManager : IDisposable
    {
        private MySqlConnection _connection;

        public DatabaseManager()
        {
            _connection = GetConnection();
            try
            {
                _connection.Open();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                throw;
            }
        }

        private MySqlConnection GetConnection()
        {
            string connectionString = ConfigManager.GetConnectionString();
            return new MySqlConnection(connectionString);
        }

        public List<User> GetUsers()
        {
            var users = new List<User>();

            using (var connection = GetConnection())
            {
                try
                {
                    connection.Open();
                    string query = "SELECT * FROM Users";

                    using (var command = new MySqlCommand(query, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            users.Add(new User
                            {
                                Id = reader.GetInt32("id"),
                                Username = reader.GetString("username"),
                                AccessToken = reader.IsDBNull(reader.GetOrdinal("access_token")) ? null : reader.GetString("access_token"),
                                ClientToken = reader.IsDBNull(reader.GetOrdinal("client_token")) ? null : reader.GetString("client_token")
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при получении пользователей: {ex.Message}");
                    throw;
                }
            }

            return users;
        }

        public void UpdateUserTokens(int userId, string accessToken, string clientToken)
        {
            using (var connection = GetConnection())
            {
                try
                {
                    connection.Open();
                    string query = "UPDATE Users SET access_token = @accessToken, client_token = @clientToken WHERE id = @userId";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@accessToken", (object)accessToken ?? DBNull.Value);
                        command.Parameters.AddWithValue("@clientToken", (object)clientToken ?? DBNull.Value);
                        command.Parameters.AddWithValue("@userId", userId);

                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при обновлении токенов пользователя: {ex.Message}");
                    throw;
                }
            }
        }

        public void Dispose()
        {
            if (_connection != null && _connection.State != ConnectionState.Closed)
            {
                _connection.Close();
                _connection.Dispose();
            }
        }

    }
}
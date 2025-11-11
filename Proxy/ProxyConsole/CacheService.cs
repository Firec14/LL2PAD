using System;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;

namespace Proxy
{
    public class CacheService
    {
        private readonly string connectionString;
        private const int CacheTTL = 240; // 4 minutes

        public CacheService(string dbPath)
        {
            connectionString = $"Data Source={dbPath};";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var createTable = @"
                CREATE TABLE IF NOT EXISTS Cache (
                    CacheKey TEXT PRIMARY KEY,
                    Content TEXT NOT NULL,
                    Timestamp INTEGER NOT NULL
                )";

            using var command = new SqliteCommand(createTable, connection);
            command.ExecuteNonQuery();
        }

        public async Task<string> GetAsync(string key)
        {
            await CleanupExpiredAsync(); // curăță intrările expirate înainte de citire

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var query = "SELECT Content, Timestamp FROM Cache WHERE CacheKey = @Key";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Key", key);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var timestamp = reader.GetInt64(1);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                if (now - timestamp < CacheTTL)
                {
                    return reader.GetString(0);
                }
            }

            return null;
        }

        public async Task SetAsync(string key, string content)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var query = @"INSERT OR REPLACE INTO Cache (CacheKey, Content, Timestamp) 
                         VALUES (@Key, @Content, @Timestamp)";

            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Key", key);
            command.Parameters.AddWithValue("@Content", content);
            command.Parameters.AddWithValue("@Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteAsync(string key)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var query = "DELETE FROM Cache WHERE CacheKey = @Key";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Key", key);
            await command.ExecuteNonQueryAsync();
        }

        // Șterge toate intrările expirate
        public async Task CleanupExpiredAsync()
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var query = "DELETE FROM Cache WHERE @Now - Timestamp >= @TTL";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Now", now);
            command.Parameters.AddWithValue("@TTL", CacheTTL);

            var deletedRows = await command.ExecuteNonQueryAsync();
            if (deletedRows > 0)
                Console.WriteLine($"[CACHE CLEANUP] Deleted {deletedRows} expired entries");
        }
    }
}

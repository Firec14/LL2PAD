using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace DataWarehouse
{
    // Enum pentru tipul de server
    public enum ServerType
    {
        Master,
        Slave
    }

    public class DatabaseService
    {
        private readonly string connectionString;
        private readonly ServerType serverType;
        private readonly string masterDbPath;
        private readonly string slaveDbPath;
        private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        // Constructor pentru Master
        public DatabaseService(string dbPath, ServerType type = ServerType.Master)
        {
            serverType = type;
            
            if (serverType == ServerType.Master)
            {
                masterDbPath = dbPath;
                connectionString = $"Data Source={masterDbPath};";
            }
            else
            {
                slaveDbPath = dbPath;
                connectionString = $"Data Source={slaveDbPath};";
            }

            InitializeDatabase();
        }

             // Constructor pentru Slave cu referință la Master
        public DatabaseService(string slaveDbPath, string masterDbPath)
        {
            serverType = ServerType.Slave;
            this.slaveDbPath = slaveDbPath;
            this.masterDbPath = masterDbPath;
            connectionString = $"Data Source={slaveDbPath};";

            InitializeDatabase();  // tabelul e creat aici

            // Replicare periodică
            Task.Run(() => ReplicateFromMaster());
        }


        private void InitializeDatabase()
        {
            string dbPath = serverType == ServerType.Master ? masterDbPath : slaveDbPath;

            if (!File.Exists(dbPath))
            {
                File.Create(dbPath).Close();
            }

            using var connection = new SqliteConnection($"Data Source={dbPath};");
            connection.Open();

            var createTable = @"
                CREATE TABLE IF NOT EXISTS Employees (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Position TEXT NOT NULL,
                    Salary REAL NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ReplicationLog (
                    LogId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Operation TEXT NOT NULL,
                    EmployeeId INTEGER,
                    Timestamp INTEGER NOT NULL
                );";

            using var command = new SqliteCommand(createTable, connection);
            command.ExecuteNonQuery();
        }



        // ===== READ OPERATIONS (Slave) =====
        public async Task<Employee> GetByIdAsync(int id)
        {
            if (serverType == ServerType.Master)
            {
                throw new InvalidOperationException("Master server should not handle read operations. Use Slave servers.");
            }

            await semaphore.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                var query = "SELECT * FROM Employees WHERE Id = @Id";
                using var command = new SqliteCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new Employee
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Position = reader.GetString(2),
                        Salary = reader.GetDecimal(3)
                    };
                }
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task<List<Employee>> GetAllAsync(int offset, int limit)
        {
            if (serverType == ServerType.Master)
            {
                throw new InvalidOperationException("Master server should not handle read operations. Use Slave servers.");
            }

            await semaphore.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                var query = "SELECT * FROM Employees LIMIT @Limit OFFSET @Offset";
                using var command = new SqliteCommand(query, connection);
                command.Parameters.AddWithValue("@Limit", limit);
                command.Parameters.AddWithValue("@Offset", offset);

                var employees = new List<Employee>();
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    employees.Add(new Employee
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Position = reader.GetString(2),
                        Salary = reader.GetDecimal(3)
                    });
                }
                return employees;
            }
            finally
            {
                semaphore.Release();
            }
        }

        // ===== WRITE OPERATIONS (Master) =====
        public async Task<int> AddAsync(Employee employee)
        {
            if (serverType == ServerType.Slave)
            {
                throw new InvalidOperationException("Slave server cannot perform write operations. Use Master server.");
            }

            await semaphore.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                var query = @"INSERT INTO Employees (Name, Position, Salary) 
                            VALUES (@Name, @Position, @Salary);
                            SELECT last_insert_rowid();";

                using var command = new SqliteCommand(query, connection);
                command.Parameters.AddWithValue("@Name", employee.Name);
                command.Parameters.AddWithValue("@Position", employee.Position);
                command.Parameters.AddWithValue("@Salary", employee.Salary);

                var result = await command.ExecuteScalarAsync();
                int newId = Convert.ToInt32(result);

                // Log pentru replicare
                await LogReplicationAsync(connection, "INSERT", newId);

                return newId;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task UpdateAsync(Employee employee)
        {
            if (serverType == ServerType.Slave)
            {
                throw new InvalidOperationException("Slave server cannot perform write operations. Use Master server.");
            }

            await semaphore.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                var query = @"UPDATE Employees 
                            SET Name = @Name, Position = @Position, Salary = @Salary 
                            WHERE Id = @Id";

                using var command = new SqliteCommand(query, connection);
                command.Parameters.AddWithValue("@Id", employee.Id);
                command.Parameters.AddWithValue("@Name", employee.Name);
                command.Parameters.AddWithValue("@Position", employee.Position);
                command.Parameters.AddWithValue("@Salary", employee.Salary);

                await command.ExecuteNonQueryAsync();

                // Log pentru replicare
                await LogReplicationAsync(connection, "UPDATE", employee.Id);
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task DeleteAsync(int id)
        {
            if (serverType == ServerType.Slave)
            {
                throw new InvalidOperationException("Slave server cannot perform write operations. Use Master server.");
            }

            await semaphore.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                var query = "DELETE FROM Employees WHERE Id = @Id";
                using var command = new SqliteCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                await command.ExecuteNonQueryAsync();

                // Log pentru replicare
                await LogReplicationAsync(connection, "DELETE", id);
            }
            finally
            {
                semaphore.Release();
            }
        }

        // ===== REPLICATION LOGIC =====
        private async Task LogReplicationAsync(SqliteConnection connection, string operation, int employeeId)
        {
            var logQuery = @"INSERT INTO ReplicationLog (Operation, EmployeeId, Timestamp) 
                           VALUES (@Operation, @EmployeeId, @Timestamp)";

            using var command = new SqliteCommand(logQuery, connection);
            command.Parameters.AddWithValue("@Operation", operation);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.Parameters.AddWithValue("@Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            await command.ExecuteNonQueryAsync();
        }

        // Replicare periodică de la Master la Slave
        public async Task ReplicateFromMaster()
        {
            if (serverType == ServerType.Master)
            {
                throw new InvalidOperationException("Master cannot replicate from itself.");
            }

            while (true)
            {
                try
                {
                    await Task.Delay(5000); // Replicare la fiecare 5 secunde

                    // Citește înregistrări din Master
                    var masterEmployees = await GetAllEmployeesFromMaster();

                    // Sincronizează cu Slave
                    await SyncWithSlave(masterEmployees);

                    Console.WriteLine($"[REPLICATION] Slave {Path.GetFileName(slaveDbPath)} synced with Master");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[REPLICATION ERROR] {ex.Message}");
                }
            }
        }

        private async Task<List<Employee>> GetAllEmployeesFromMaster()
        {
            var masterConnection = $"Data Source={masterDbPath};";
            using var connection = new SqliteConnection(masterConnection);
            await connection.OpenAsync();

            var query = "SELECT * FROM Employees";
            using var command = new SqliteCommand(query, connection);

            var employees = new List<Employee>();
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                employees.Add(new Employee
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Position = reader.GetString(2),
                    Salary = reader.GetDecimal(3)
                });
            }

            return employees;
        }

        private async Task SyncWithSlave(List<Employee> masterEmployees)
        {
            await semaphore.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                // Șterge toate datele din Slave
                var deleteQuery = "DELETE FROM Employees";
                using var deleteCommand = new SqliteCommand(deleteQuery, connection);
                await deleteCommand.ExecuteNonQueryAsync();

                // Inserează date de la Master
                foreach (var employee in masterEmployees)
                {
                    var insertQuery = @"INSERT INTO Employees (Id, Name, Position, Salary) 
                                      VALUES (@Id, @Name, @Position, @Salary)";

                    using var insertCommand = new SqliteCommand(insertQuery, connection);
                    insertCommand.Parameters.AddWithValue("@Id", employee.Id);
                    insertCommand.Parameters.AddWithValue("@Name", employee.Name);
                    insertCommand.Parameters.AddWithValue("@Position", employee.Position);
                    insertCommand.Parameters.AddWithValue("@Salary", employee.Salary);

                    await insertCommand.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        // Helper pentru verificare tip server
        public bool IsMaster() => serverType == ServerType.Master;
        public bool IsSlave() => serverType == ServerType.Slave;
    }
}
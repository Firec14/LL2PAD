using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

namespace DataWarehouse
{
    class Program
    {
        private static DatabaseService db;
        private static int port;
        private static ServerType serverType;

        static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: dotnet run <port> <server_type>");
                Console.WriteLine("  server_type: master | slave");
                Console.WriteLine("\nExamples:");
                Console.WriteLine("  dotnet run 8081 master");
                Console.WriteLine("  dotnet run 8082 slave");
                Console.WriteLine("  dotnet run 8083 slave");
                return;
            }

            port = int.Parse(args[0]);
            var type = args[1].ToLower();

            if (type == "master")
            {
                serverType = ServerType.Master;
                db = new DatabaseService("master.db", ServerType.Master);
                Console.WriteLine($"MASTER DataWarehouse started on port {port}");
                Console.WriteLine("   Handling: PUT, POST, DELETE operations");
            }
            else if (type == "slave")
            {
                serverType = ServerType.Slave;
                db = new DatabaseService($"slave_{port}.db", "master.db");
                Console.WriteLine($"SLAVE DataWarehouse started on port {port}");
                Console.WriteLine("   Handling: GET operations");
                Console.WriteLine("   Replicating from Master every 5 seconds...");
            }
            else
            {
                Console.WriteLine("Invalid server type! Use 'master' or 'slave'");
                return;
            }

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();

            Console.WriteLine($"Listening on http://localhost:{port}/\n");

            while (true)
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
        }

        static async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                var path = request.Url.AbsolutePath;
                var method = request.HttpMethod;

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{method}] {path}");

                // Validare: Master nu acceptă GET, Slave nu acceptă scrieri
                if (serverType == ServerType.Master && method == "GET")
                {
                    response.StatusCode = 403;
                    await SendResponse(response, 
                        "{\"error\":\"Master server does not handle GET requests. Use Slave servers.\"}", 
                        "application/json");
                    return;
                }

                if (serverType == ServerType.Slave && (method == "PUT" || method == "POST" || method == "DELETE"))
                {
                    response.StatusCode = 403;
                    await SendResponse(response, 
                        "{\"error\":\"Slave server does not handle write operations. Use Master server.\"}", 
                        "application/json");
                    return;
                }

                // Routing
                if (path.StartsWith("/employee/") && method == "GET")
                {
                    await HandleGetEmployeeById(request, response);
                }
                else if (path.StartsWith("/employees") && method == "GET")
                {
                    await HandleGetAllEmployees(request, response);
                }
                else if (path.StartsWith("/employee") && method == "PUT")
                {
                    await HandleAddEmployee(request, response);
                }
                else if (path.StartsWith("/employee") && method == "POST")
                {
                    await HandleUpdateEmployee(request, response);
                }
                else if (path.StartsWith("/employee/") && method == "DELETE")
                {
                    await HandleDeleteEmployee(request, response);
                }
                else if (path == "/health" && method == "GET")
                {
                    await HandleHealthCheck(response);
                }
                else
                {
                    response.StatusCode = 404;
                    await SendResponse(response, "{\"error\":\"Not found\"}", "application/json");
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[OPERATION ERROR] {ex.Message}");
                response.StatusCode = 403;
                await SendResponse(response, $"{{\"error\":\"{ex.Message}\"}}", "application/json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                response.StatusCode = 500;
                await SendResponse(response, $"{{\"error\":\"{ex.Message}\"}}", "application/json");
            }
        }

        static async Task HandleGetEmployeeById(HttpListenerRequest request, HttpListenerResponse response)
        {
            var id = int.Parse(request.Url.Segments[2].TrimEnd('/'));
            var employee = await db.GetByIdAsync(id);

            if (employee == null)
            {
                response.StatusCode = 404;
                await SendResponse(response, "{\"error\":\"Employee not found\"}", "application/json");
                return;
            }

            var json = JsonSerializer.Serialize(employee);
            await SendResponse(response, json, "application/json");
        }

        static async Task HandleGetAllEmployees(HttpListenerRequest request, HttpListenerResponse response)
        {
            var query = request.Url.Query;
            var offset = GetQueryParam(query, "offset", 0);
            var limit = GetQueryParam(query, "limit", 10);

            var employees = await db.GetAllAsync(offset, limit);
            var json = JsonSerializer.Serialize(employees);
            await SendResponse(response, json, "application/json");
        }

        static async Task HandleAddEmployee(HttpListenerRequest request, HttpListenerResponse response)
        {
            using var reader = new StreamReader(request.InputStream);
            var body = await reader.ReadToEndAsync();
            var employee = JsonSerializer.Deserialize<Employee>(body);

            var id = await db.AddAsync(employee);
            var json = JsonSerializer.Serialize(new { id, message = "Employee added to Master. Replicating to Slaves..." });
            await SendResponse(response, json, "application/json");
        }

        static async Task HandleUpdateEmployee(HttpListenerRequest request, HttpListenerResponse response)
        {
            using var reader = new StreamReader(request.InputStream);
            var body = await reader.ReadToEndAsync();
            var employee = JsonSerializer.Deserialize<Employee>(body);

            await db.UpdateAsync(employee);
            var json = JsonSerializer.Serialize(new { message = "Employee updated on Master. Replicating to Slaves..." });
            await SendResponse(response, json, "application/json");
        }

        static async Task HandleDeleteEmployee(HttpListenerRequest request, HttpListenerResponse response)
        {
            var id = int.Parse(request.Url.Segments[2].TrimEnd('/'));
            await db.DeleteAsync(id);
            var json = JsonSerializer.Serialize(new { message = "Employee deleted from Master. Replicating to Slaves..." });
            await SendResponse(response, json, "application/json");
        }

        static async Task HandleHealthCheck(HttpListenerResponse response)
        {
            var health = new
            {
                status = "healthy",
                serverType = serverType.ToString(),
                port = port,
                timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(health);
            await SendResponse(response, json, "application/json");
        }

        static async Task SendResponse(HttpListenerResponse response, string content, string contentType)
        {
            response.ContentType = contentType;
            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }

        static int GetQueryParam(string query, string param, int defaultValue)
        {
            var match = System.Text.RegularExpressions.Regex.Match(query, $@"{param}=(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : defaultValue;
        }
    }
}
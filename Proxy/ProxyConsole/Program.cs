using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Proxy;

namespace Proxy
{
    class Program
    {
        private static CacheService? cache;
        private static LoadBalancer? loadBalancer;
        private const int CacheCleanupInterval = 60; // secunde

        static async Task Main(string[] args)
        {
            cache = new CacheService("proxy_cache.db");
            
            // Read warehouse URLs from environment variables or use defaults
            string masterUrl = Environment.GetEnvironmentVariable("MASTER_URL") ?? "http://localhost:8081";
            string slave1Url = Environment.GetEnvironmentVariable("SLAVE1_URL") ?? "http://localhost:8082";
            string slave2Url = Environment.GetEnvironmentVariable("SLAVE2_URL") ?? "http://localhost:8083";
            
            loadBalancer = new LoadBalancer(
                masters: new[] { masterUrl },
                slaves: new[] { slave1Url, slave2Url }
            );

            // Pornim cleanup periodic
            _ = Task.Run(() => RunCacheCleanupAsync());

            var listener = new HttpListener();
            listener.Prefixes.Add("http://+:8080/");
            listener.Start();

            Console.WriteLine("PROXY Server started on http://localhost:8080/");
            Console.WriteLine($"Forwarding to warehouses: {masterUrl} (Master), {slave1Url}, {slave2Url} (Slaves)\n");

            while (true)
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
        }

        // Cleanup periodic
        private static async Task RunCacheCleanupAsync()
        {
            while (true)
            {
                try
                {
                    await cache?.CleanupExpiredAsync()!;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CACHE CLEANUP ERROR] {ex.Message}");
                }
                await Task.Delay(CacheCleanupInterval * 1000);
            }
        }

        static async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                var cacheKey = $"{request.HttpMethod}:{request.Url?.PathAndQuery ?? "/"}";
                string warehouse;
                if (request.HttpMethod == "GET")
                    warehouse = loadBalancer?.GetNextSlave() ?? throw new InvalidOperationException("LoadBalancer not initialized");
                else
                    warehouse = loadBalancer?.GetMaster() ?? throw new InvalidOperationException("LoadBalancer not initialized");

                var warehouseUrl = warehouse + (request.Url?.PathAndQuery ?? "/");
                
                Console.WriteLine($"[{request.HttpMethod}] → {warehouseUrl}");


        using var client = new HttpClient();
        HttpResponseMessage warehouseResponse;

        if (request.HttpMethod == "GET")
        {
            var cached = await cache?.GetAsync(cacheKey)!;
            if (cached != null)
            {
                Console.WriteLine($"[CACHE HIT] {cacheKey}");
                await SendResponse(response, cached, "application/json");
                return;
            }
            Console.WriteLine($"[CACHE MISS] {cacheKey}");
            warehouseResponse = await client.GetAsync(warehouseUrl);
        }
        else if (request.HttpMethod == "PUT" || request.HttpMethod == "POST")
        {
            using var reader = new StreamReader(request.InputStream);
            var body = await reader.ReadToEndAsync();
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var employee = System.Text.Json.JsonSerializer.Deserialize<Employee>(body, options);
                if (employee != null)
                {
                    var key = $"GET:/employee/{employee.Id}";
                    await cache?.DeleteAsync(key)!;
                    Console.WriteLine($"[CACHE INVALIDATED] {key}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CACHE INVALIDATION ERROR] {ex.Message}");
            }

            warehouseResponse = request.HttpMethod == "PUT"
                ? await client.PutAsync(warehouseUrl, content)
                : await client.PostAsync(warehouseUrl, content);
        }
        else if (request.HttpMethod == "DELETE")
        {
            // Trimite cererea DELETE la warehouse
            warehouseResponse = await client.DeleteAsync(warehouseUrl);

            // Șterge cache-ul relevant
            var segments = request.Url?.Segments;
            if (segments != null && segments.Length > 2)
            {
                var key = $"GET:/employee/{segments[2].TrimEnd('/')}";
                await cache?.DeleteAsync(key)!;
                Console.WriteLine($"[CACHE INVALIDATED] {key}");
            }
        }
        else
        {
            response.StatusCode = 405;
            await SendResponse(response, "Method not allowed", "text/plain");
            return;
        }

        var result = await warehouseResponse.Content.ReadAsStringAsync();

        if (request.HttpMethod == "GET" && warehouseResponse.IsSuccessStatusCode)
        {
            await cache?.SetAsync(cacheKey, result)!;
        }

        response.StatusCode = (int)warehouseResponse.StatusCode;
        await SendResponse(response, result, "application/json");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex.Message}");
        response.StatusCode = 502;
        await SendResponse(response, $"Proxy error: {ex.Message}", "text/plain");
    }
}


        static async Task SendResponse(HttpListenerResponse response, string content, string contentType)
        {
            response.ContentType = contentType;
            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }
    }
}

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Client
{
    public class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Position { get; set; }
        public decimal Salary { get; set; }
    }

    class Program
    {
        private static readonly HttpClient client = new HttpClient();
        private static readonly string proxyUrl = "http://localhost:8080";

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== CLIENT APPLICATION ===\n");

            while (true)
            {
                Console.WriteLine("\n--- MENU ---");
                Console.WriteLine("1. Get Employee by ID");
                Console.WriteLine("2. Get All Employees");
                Console.WriteLine("3. Add New Employee");
                Console.WriteLine("4. Update Employee");
                Console.WriteLine("5. Delete Employee");
                Console.WriteLine("6. Exit");
                Console.Write("\nSelect option: ");

                var option = Console.ReadLine();

                try
                {
                    switch (option)
                    {
                        case "1":
                            await GetEmployeeById();
                            break;
                        case "2":
                            await GetAllEmployees();
                            break;
                        case "3":
                            await AddEmployee();
                            break;
                        case "4":
                            await UpdateEmployee();
                            break;
                        case "5":
                            await DeleteEmployee();
                            break;
                        case "6":
                            return;
                        default:
                            Console.WriteLine("Invalid option!");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static async Task GetEmployeeById()
        {
            Console.Write("Enter Employee ID: ");
            var id = Console.ReadLine();

            var response = await client.GetAsync($"{proxyUrl}/employee/{id}");
            var content = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"\nResponse ({response.StatusCode}):");
            Console.WriteLine(content);
        }

        static async Task GetAllEmployees()
        {
            Console.Write("Offset (default 0): ");
            var offset = Console.ReadLine();
            Console.Write("Limit (default 10): ");
            var limit = Console.ReadLine();

            var url = $"{proxyUrl}/employees?offset={offset}&limit={limit}";
            var response = await client.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"\nResponse ({response.StatusCode}):");
            Console.WriteLine(content);
        }

        static async Task AddEmployee()
        {
            Console.WriteLine("\n--- Add New Employee ---");
            Console.Write("Name: ");
            var name = Console.ReadLine();
            Console.Write("Position: ");
            var position = Console.ReadLine();
            Console.Write("Salary: ");
            var salary = decimal.Parse(Console.ReadLine());

            var employee = new Employee
            {
                Name = name,
                Position = position,
                Salary = salary
            };

            var json = JsonSerializer.Serialize(employee);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PutAsync($"{proxyUrl}/employee", content);
            var result = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"\nResponse ({response.StatusCode}):");
            Console.WriteLine(result);
        }

        static async Task UpdateEmployee()
        {
            Console.Write("Employee ID to update: ");
            var id = int.Parse(Console.ReadLine());
            Console.Write("New Name: ");
            var name = Console.ReadLine();
            Console.Write("New Position: ");
            var position = Console.ReadLine();
            Console.Write("New Salary: ");
            var salary = decimal.Parse(Console.ReadLine());

            var employee = new Employee
            {
                Id = id,
                Name = name,
                Position = position,
                Salary = salary
            };

            var json = JsonSerializer.Serialize(employee);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{proxyUrl}/employee", content);
            var result = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"\nResponse ({response.StatusCode}):");
            Console.WriteLine(result);
        }

        static async Task DeleteEmployee()
        {
            Console.Write("Employee ID to delete: ");
            var id = Console.ReadLine();

            // Se folosește HttpRequestMessage pentru DELETE
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{proxyUrl}/employee/{id}");
            var response = await client.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"\nResponse ({response.StatusCode}):");
            Console.WriteLine(result);
        }
    }
}

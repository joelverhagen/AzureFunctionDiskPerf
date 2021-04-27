using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;

namespace ConsoleApp1
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var endpoint = "https://<function app name>.azurewebsites.net/api";
            var code = "<function app access key>";

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            var inputs = GetTestInputs(endpoint, code).ToList();

            // Warm-up
            Console.WriteLine("Warming up...");
            Console.WriteLine(JsonSerializer.Serialize(await WarmUpAsync(httpClient, endpoint, code, "HOME")));
            Console.WriteLine(JsonSerializer.Serialize(await WarmUpAsync(httpClient, endpoint, code, "TEMP")));
            Console.WriteLine();

            var iterations = 100;
            for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                var input = inputs[inputIndex];

                for (var iterationIndex = 0; iterationIndex < iterations; iterationIndex++)
                {
                    Console.Write($"Running test {inputIndex + 1} of {inputs.Count}, iteration {iterationIndex + 1} of {iterations}...");
                    try
                    {
                        var output = await ExecuteAsync(httpClient, input);
                        AppendOutput(output);
                        Console.WriteLine($" done in {output.ClientElapsedMs}ms");
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode.HasValue && ex.StatusCode.Value >= (HttpStatusCode)500)
                    {
                        Console.WriteLine($" server error.");
                        Console.WriteLine(input);
                        throw;
                    }
                }
            }
        }

        private static async Task<TestOutput> WarmUpAsync(HttpClient httpClient, string endpoint, string code, string testDir)
        {
            return await ExecuteAsync(httpClient, new TestInput
            {
                Code = code,
                Function = GetFunctions().First(),
                Endpoint = endpoint,
                AppBufferSize = 4096,
                FileStreamBufferSize = 4096,
                DataSize = 4096,
                TestDir = testDir,
                SetLength = true,
            });
        }

        private static void AppendOutput(TestOutput output)
        {
            var path = "results.csv";
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = !File.Exists(path),
            };

            using var stream = File.Open(path, FileMode.Append);
            using var writer = new StreamWriter(stream);
            using var csv = new CsvWriter(writer, config);

            csv.WriteRecords(new[] { output });
        }

        static async Task<TestOutput> ExecuteAsync(HttpClient httpClient, TestInput input)
        {
            var parameters = new Dictionary<string, object>
            {
                { "code", input.Code },
                { "testDir", input.TestDir },
                { "dataSize", input.DataSize },
                { "appBufferSize", input.AppBufferSize },
                { "fileStreamBufferSize", input.FileStreamBufferSize },
                { "setLength", input.SetLength },
            };

            var queryString = string.Join("&", parameters.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value.ToString())}"));
            var url = new UriBuilder(input.Endpoint.TrimEnd('/') + '/' + input.Function) { Query = queryString };

            var sw = Stopwatch.StartNew();
            var json = await httpClient.GetStringAsync(url.Uri);
            sw.Stop();

            var response = JsonSerializer.Deserialize<Response>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return new TestOutput
            {
                Id = Guid.NewGuid(),
                Input = new TestInput
                {
                    Function = input.Function,
                    AppBufferSize = response.AppBufferSize,
                    DataSize = response.DataSize,
                    FileStreamBufferSize = response.FileStreamBufferSize,
                    TestDir = response.TestDir,
                    SetLength = response.SetLength,
                },
                ClientElapsedMs = sw.Elapsed.TotalMilliseconds,
                ServerElapsedMs = response.ElapsedMs,
            };
        }

        private const int MiB = 1024 * 1024;

        static IEnumerable<TestInput> GetTestInputs(string endpoint, string code)
        {
            var inputs =
                from function in GetFunctions()
                from testDir in GetTestDirs()
                from setLength in GetSetLengths()
                from appBufferSize in GetAppBufferSizes()
                from fileStreamBufferSize in GetFileStreamBufferSizes()
                from dataSize in GetDataSizes()
                select new TestInput
                {
                    Endpoint = endpoint,
                    Function = function,
                    Code = code,
                    TestDir = testDir,
                    AppBufferSize = appBufferSize,
                    FileStreamBufferSize = fileStreamBufferSize,
                    DataSize = dataSize,
                    SetLength = setLength,
                };

            return inputs
                .Distinct()
                .OrderByDescending(x => x.TestDir)
                .ThenByDescending(x => x.DataSize)
                .ThenByDescending(x => x.AppBufferSize)
                .ThenByDescending(x => x.FileStreamBufferSize)
                .ThenByDescending(x => x.SetLength);
        }

        static IEnumerable<string> GetFunctions()
        {
            yield return "FileWritePerf";
            // yield return "DotnetDefaults";
        }

        static IEnumerable<string> GetTestDirs()
        {
            yield return "TEMP";
            yield return "HOME";
        }

        static IEnumerable<bool> GetSetLengths()
        {
            // yield return false;
            yield return true;
        }

        static IEnumerable<int> GetAppBufferSizes()
        {
            yield return 4 * MiB;
            /*
            yield return 2 * MiB;
            yield return 4 * MiB;
            yield return 8 * MiB;
            yield return 16 * MiB;
            yield return 32 * MiB;
            yield return 64 * MiB;
            yield return 128 * MiB;
            yield return 256 * MiB;
            */
        }

        static IEnumerable<int> GetFileStreamBufferSizes()
        {
            yield return 4096;
        }

        static IEnumerable<int> GetDataSizes()
        {
            yield return 1 * MiB;
            yield return 2 * MiB;
            yield return 4 * MiB;
            yield return 8 * MiB;
            yield return 16 * MiB;
            yield return 32 * MiB;
            yield return 64 * MiB;
            yield return 128 * MiB;
            yield return 256 * MiB;
        }
    }
}

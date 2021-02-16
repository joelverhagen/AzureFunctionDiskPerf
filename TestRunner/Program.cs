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
            var endpoint = "https://<function app name>.azurewebsites.net/api/FileWritePerf";
            var code = "<function app access key>";

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            var inputs = GetTestInputs(endpoint, code).ToList();

            // Warm-up
            Console.WriteLine("Warming up...");
            Console.WriteLine((await WarmUpAsync(httpClient, endpoint, code, "HOME")).);
            Console.WriteLine(await WarmUpAsync(httpClient, endpoint, code, "TEMP"));
            Console.WriteLine();

            var skippedInputs = SkippedInputs.Select(x => x with { Endpoint = endpoint, Code = code }).ToHashSet();

            var iterations = 10;
            for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                if (inputIndex < 278)
                {
                    continue;
                }

                var input = inputs[inputIndex];

                if (skippedInputs.Contains(input))
                {
                    continue;
                }

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
                Endpoint = endpoint,
                AppBufferSize = 4096,
                FileStreamBufferSize = 4096,
                DataSize = 4096,
                TestDir = testDir,
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
            };

            var queryString = string.Join("&", parameters.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value.ToString())}"));
            var url = new UriBuilder(input.Endpoint) { Query = queryString };
            var sw = Stopwatch.StartNew();
            var json = await httpClient.GetStringAsync(url.Uri);
            sw.Stop();

            var response = JsonSerializer.Deserialize<Response>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return new TestOutput
            {
                Id = Guid.NewGuid(),
                Input = new TestInput
                {
                    AppBufferSize = response.AppBufferSize,
                    DataSize = response.DataSize,
                    FileStreamBufferSize = response.FileStreamBufferSize,
                    TestDir = response.TestDir,
                },
                ClientElapsedMs = sw.Elapsed.TotalMilliseconds,
                ServerElapsedMs = response.ElapsedMs,
            };
        }

        private const int MiB = 1024 * 1024;

        /// <summary>
        /// These inputs time out.
        /// </summary>
        private static IReadOnlyList<TestInput> SkippedInputs = new[]
        {
            new TestInput
            {
                AppBufferSize = 4096,
                DataSize = 64 * MiB,
                FileStreamBufferSize = 4096,
                TestDir = "HOME",
            },
            new TestInput
            {
                AppBufferSize = 4096,
                DataSize = 64 * MiB,
                FileStreamBufferSize = 1,
                TestDir = "HOME",
            },
            new TestInput
            {
                AppBufferSize = 4096,
                DataSize = 32 * MiB,
                FileStreamBufferSize = 4096,
                TestDir = "HOME",
            },
            new TestInput
            {
                AppBufferSize = 4096,
                DataSize = 32 * MiB,
                FileStreamBufferSize = 1,
                TestDir = "HOME",
            },
        };

        static IEnumerable<TestInput> GetTestInputs(string endpoint, string code)
        {
            var inputs =
                from testDir in new[] { "TEMP", "HOME" }
                from appBufferSize in GetAppBufferSizes()
                from fileStreamBufferSize in GetFileStreamBufferSizes()
                from dataSize in GetDataSizes()
                select new TestInput
                {
                    Endpoint = endpoint,
                    Code = code,
                    TestDir = testDir,
                    AppBufferSize = appBufferSize,
                    FileStreamBufferSize = fileStreamBufferSize > 0 ? fileStreamBufferSize : appBufferSize,
                    DataSize = dataSize,
                };

            return inputs
                .Distinct()
                .OrderByDescending(x => x.TestDir)
                .ThenByDescending(x => x.DataSize)
                .ThenByDescending(x => x.AppBufferSize)
                .ThenByDescending(x => x.FileStreamBufferSize);
        }

        static IEnumerable<int> GetAppBufferSizes()
        {
            // Match the file stream buffer size
            foreach (var size in GetFileStreamBufferSizes())
            {
                if (size >= 4096)
                {
                    yield return size;
                }
            }

            yield return MiB;
            yield return 2 * MiB;
            yield return 4 * MiB;
        }

        static IEnumerable<int> GetFileStreamBufferSizes()
        {
            yield return -1; // match the app buffer size
            yield return 1; // minimum
            yield return 4096; // 4 KiB, default for FileStream
            yield return 80 * 1024; // 80 KiB, default for CopyToAsync
            yield return 4 * 1024 * 1024; // 4 MiB, default for Azure File Share client: https://docs.microsoft.com/en-us/dotnet/api/azure.storage.files.shares.models.sharefileopenwriteoptions.buffersize?view=azure-dotnet
        }

        static IEnumerable<int> GetDataSizes()
        {
            yield return 0;
            yield return 1024;
            yield return MiB / 2;
            var dataSizeMiB = 1;
            do
            {
                yield return MiB * dataSizeMiB;
                dataSizeMiB *= 2;
            }
            while (dataSizeMiB <= 64);
        }
    }
}

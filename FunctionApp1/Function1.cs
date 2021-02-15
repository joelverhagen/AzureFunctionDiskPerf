using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace FunctionApp1
{
    public static class Function1
    {
        [FunctionName("FileWritePerf")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            var dataSize = int.Parse(req.Query["dataSize"]);
            var appBufferSize = int.Parse(req.Query["appBufferSize"]);
            var fileStreamBufferSize = int.Parse(req.Query["fileStreamBufferSize"]);
            var useHome = bool.Parse(req.Query["useHome"]);

            string baseDir;
            if (useHome && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOME")))
            {
                baseDir = Environment.ExpandEnvironmentVariables(Path.Combine("%HOME%", "Knapcode.FunctionDiskPerf", "temp"));
            }
            else
            {
                useHome = false;
                baseDir = Environment.ExpandEnvironmentVariables(Path.Combine(Path.GetTempPath(), "Knapcode.FunctionDiskPerf", "temp"));
            }

            baseDir = Path.GetFullPath(baseDir);
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            var tempPath = Path.Combine(baseDir, Guid.NewGuid().ToString());

            var sw = Stopwatch.StartNew();
            using (var dest = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                fileStreamBufferSize,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            {
                var random = new Random(0);
                var appBuffer = new byte[appBufferSize];
                var written = 0;
                while (written < dataSize)
                {
                    var toWrite = Math.Min(appBufferSize, dataSize - written);
                    random.NextBytes(appBuffer.AsSpan(0, toWrite));
                    await dest.WriteAsync(appBuffer, 0, toWrite);
                    written += toWrite;
                }
            }

            sw.Stop();

            return new JsonResult(new
            {
                useHome,
                tempPath,
                dataSize,
                appBufferSize,
                fileStreamBufferSize,
                elapsedMs = sw.Elapsed.TotalMilliseconds,
            });
        }
    }
}

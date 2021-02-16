using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FunctionApp1
{
    public static class Function1
    {
        [FunctionName("FileWritePerf")]
        public static async Task<IActionResult> FileWritePerf([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            var dataSize = int.Parse(req.Query["dataSize"]);
            var appBufferSize = int.Parse(req.Query["appBufferSize"]);
            var fileStreamBufferSize = int.Parse(req.Query["fileStreamBufferSize"]);
            var setLength = bool.Parse(req.Query["setLength"]);
            var testDir = Enum.Parse<TestDir>(req.Query["testDir"], ignoreCase: true);
            var testPath = Path.Combine(GetTempDir(testDir), Guid.NewGuid().ToString());

            var sw = Stopwatch.StartNew();
            using (var dest = new FileStream(
                testPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                fileStreamBufferSize,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            {
                if (setLength)
                {
                    dest.SetLength(dataSize);
                }

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
                testDir = testDir.ToString(),
                testPath,
                dataSize,
                appBufferSize,
                fileStreamBufferSize,
                elapsedMs = sw.Elapsed.TotalMilliseconds,
            });
        }

        [FunctionName("DotnetDefaults")]
        public static async Task<IActionResult> DotnetDefaults([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            var dataSize = int.Parse(req.Query["dataSize"]);
            var testDir = Enum.Parse<TestDir>(req.Query["testDir"], ignoreCase: true);
            var testPath = Path.Combine(GetTempDir(testDir), Guid.NewGuid().ToString());

            var sw = Stopwatch.StartNew();
            using (var dest = new FileStream(
                testPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 4 * 1024, // Default
                FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            {
                var random = new Random(0);
                var buffer = new byte[dataSize];
                random.NextBytes(buffer);
                var source = new MemoryStream(buffer);

                await source.CopyToAsync(dest);
            }

            sw.Stop();

            return new JsonResult(new
            {
                testDir = testDir.ToString(),
                testPath,
                dataSize,
                elapsedMs = sw.Elapsed.TotalMilliseconds,
            });
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetDiskFreeSpaceEx(
            string lpDirectoryName,
            out ulong lpFreeBytesAvailable,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        [FunctionName("DiskSpace")]
        public static IActionResult DiskSpace([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            try
            {
                var tempDir = GetTempDir(TestDir.Temp);
                GetDiskFreeSpaceEx(tempDir, out var tempFreeBytesAvailable, out var tempTotalNumberOfBytes, out var tempTotalNumberOfFreeBytes);

                var homeDir = GetTempDir(TestDir.Home);
                GetDiskFreeSpaceEx(homeDir, out var homeFreeBytesAvailable, out var homeTotalNumberOfBytes, out var homeTotalNumberOfFreeBytes);

                return new JsonResult(new
                {
                    tempDir,
                    tempFreeBytesAvailable,
                    tempTotalNumberOfBytes,
                    tempTotalNumberOfFreeBytes,

                    homeDir,
                    homeFreeBytesAvailable,
                    homeTotalNumberOfBytes,
                    homeTotalNumberOfFreeBytes,
                });
            }
            catch (Exception ex)
            {
                return new OkObjectResult(ex.ToString());
            }
        }

        private enum TestDir
        {
            Temp,
            Home,
        }

        private static string GetTempDir(TestDir testDir)
        {
            string baseDir;
            switch (testDir)
            {
                case TestDir.Home when !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOME")):
                    baseDir = Environment.GetEnvironmentVariable("HOME");
                    break;
                default:
                    baseDir = Path.GetTempPath();
                    break;
            }

            var tempDir = Path.Combine(Path.GetFullPath(baseDir), "AzureFunctionDiskPerf");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            return tempDir;
        }
    }
}

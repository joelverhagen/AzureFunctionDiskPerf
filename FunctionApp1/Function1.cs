using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Buffers;

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

                using var source = GetSource(dataSize);

                var appBuffer = ArrayPool<byte>.Shared.Rent(appBufferSize);
                try
                {
                    appBufferSize = appBuffer.Length;
                    int read;
                    do
                    {
                        read = await source.ReadAsync(appBuffer, 0, appBuffer.Length);
                        await dest.WriteAsync(appBuffer, 0, read);
                    }
                    while (read > 0);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(appBuffer);
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
                setLength,
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
                using var source = GetSource(dataSize);
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

        private static UnseekableMemoryStream GetSource(int dataSize)
        {
            var random = new Random(0);
            var buffer = ArrayPool<byte>.Shared.Rent(dataSize);
            random.NextBytes(buffer.AsSpan(0, dataSize));
            return new UnseekableMemoryStream(buffer, 0, dataSize);
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

        private class UnseekableMemoryStream : Stream
        {
            private readonly byte[] _buffer;
            private readonly MemoryStream _inner;

            public UnseekableMemoryStream(byte[] buffer, int index, int count)
            {
                _buffer = buffer;
                _inner = new MemoryStream(buffer, index, count);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _inner.Length;
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    ArrayPool<byte>.Shared.Return(_buffer);
                    _inner.Dispose();
                }
            }

            public override void Flush()
            {
                throw new NotSupportedException();
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}

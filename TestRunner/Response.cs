namespace ConsoleApp1
{
    public class Response
    {
        public string TestDir { get; init; }
        public string TempPath { get; init; }
        public int DataSize { get; init; }
        public int AppBufferSize { get; init; }
        public int FileStreamBufferSize { get; init; }
        public double ElapsedMs { get; init; }
        public bool SetLength { get; init; }
    }
}

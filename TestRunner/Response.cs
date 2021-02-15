namespace ConsoleApp1
{
    public class Response
    {
        public bool UseHome { get; init; }
        public string TempPath { get; init; }
        public int DataSize { get; init; }
        public int AppBufferSize { get; init; }
        public int FileStreamBufferSize { get; init; }
        public double ElapsedMs { get; init; }
    }
}

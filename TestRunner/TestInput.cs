namespace ConsoleApp1
{
    public record TestInput
    {
        public string Endpoint { get; init; }
        public string Code { get; init; }
        public bool UseHome { get; init; }
        public int DataSize { get; init; }
        public int AppBufferSize { get; init; }
        public int FileStreamBufferSize { get; init; }
    }
}

using System;

namespace ConsoleApp1
{
    public class TestOutput
    {
        public Guid Id { get; init; }
        public TestInput Input { get; init; }
        public double ClientElapsedMs { get; init; }
        public double ServerElapsedMs { get; init; }
    }
}

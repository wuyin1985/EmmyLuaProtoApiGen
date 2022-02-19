using System;

namespace runner
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine($"start gen lua pb api tip from {args[0]} to {args[1]}, config: {args[2]}");
            PbUtils.GenPbAPI(args[0], args[1], args[2]);
            Console.WriteLine("gen lua pb api tip complete");
        }
    }
}
using System;

namespace runner
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine($"start gen lua pb api tip from {args[0]} to {args[1]}");
            PbUtils.GenPbAPI(args[0], args[1]);
            Console.WriteLine("gen lua pb api tip complete");
        }
    }
}
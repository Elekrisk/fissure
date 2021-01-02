using System;
using System.IO;
using System.Linq;

namespace fissure
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("usage: fissure <file>");
                return;
            }
            var path = args[0];
            var code = File.ReadAllText(path);
            var lexer = new Lexer(path, code);
            var parser = new Parser(lexer);
            parser.ParseProgram();
        }
    }
}

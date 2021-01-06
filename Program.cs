using System;
using System.Collections.Generic;
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
            var tokens = lexer.Lex();
            var parser = new Parser(path, tokens);
            var program = parser.ParseProgram();
            var executor = new Executor();
            if (executor.ExecProgram(program.Unwrap()) is ErrorValue e)
            {
                Console.WriteLine($"Error: {e.Error}");
            }
        }
    }
}

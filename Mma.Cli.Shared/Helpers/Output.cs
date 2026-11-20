using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Helpers
{
    public enum OutputClass : int
    {
        Defaut = 0,
        Success,
        Warning,
        Error
    }

    public class Output
    {
        public static void PrintF(OutputClass cls, string msg)
        {
            switch (cls)
            {
                case OutputClass.Success:
                    Success(msg);
                    break;
                case OutputClass.Warning:
                    Warning(msg);
                    break;
                case OutputClass.Error:
                    Error(msg);
                    break;
                default:
                    Default(msg);
                    break;
            }
        }

        public static void Default(string msg)
        {
            Console.WriteLine(msg);
        }
        public static void Success(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(msg);
        }

        public static void Warning(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(msg);
        }

        public static void Error(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);
        }


    }
}

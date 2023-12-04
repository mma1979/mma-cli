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
            _ = cls switch
            {
                OutputClass.Success => Success(msg),
                OutputClass.Warning => Warning(msg),
                OutputClass.Error => Error(msg),
                _ => Default(msg)
            };

        }

        public static string Default(string msg)
        {
            Console.WriteLine(msg);

            return "Default";
        }
        public static string Success(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(msg);

            return "Success";
        }

        public static string Warning(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(msg);

            return "Warning";
        }

        public static string Error(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);

            return "Error";
        }


    }
}

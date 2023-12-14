using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Extensions
{
    public static  class StringExtensions
    {
        public static string ToEntityName(this string dbTableName)
        {
            return dbTableName.EndsWith("ies") ?
                dbTableName.Replace("ies", "y") : dbTableName.EndsWith("ses") ?
                dbTableName.Replace("ses", "s") : dbTableName.TrimEnd('s');
        }
    }
}

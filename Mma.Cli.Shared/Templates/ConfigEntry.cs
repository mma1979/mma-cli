using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates
{
    public static class ConfigEntry
    {
        public const string Template = "            modelBuilder.ApplyConfiguration(new $EntityNameConfig());";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates
{
    public static class DbSetEntry
    {
        public const string Template = "            public virtual DbSet<$EntityName> $EntitySetName { get; set; }";
    }
}

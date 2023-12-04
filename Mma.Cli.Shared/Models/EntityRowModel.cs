using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Models
{
    public class EntityRowModel
    {
        public int Id { get; set; }
        public string? ColumnName { get; set; }

        public string? DataType { get; set; }

        public bool Nullable { get; set; } = false;
        public bool Applied { get; set; } = false;
        public bool IsForeignKey { get; set; } = false;

        public int? EntityId { get; set; }

        public virtual EntityModel? Entity { get; set; }
    }
}

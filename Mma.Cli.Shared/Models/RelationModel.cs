using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Models
{
    public class RelationModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool Applied { get; set; } = false;

        public int? ParentId { get; set; }
        public virtual EntityModel? ParentEntity { get; set; }

        public int? ChildId { get; set; }
        public virtual EntityModel? ChiledEntity { get; set; }
    }
}

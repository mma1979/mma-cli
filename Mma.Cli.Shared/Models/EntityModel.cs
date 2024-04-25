using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Models
{
    public class EntityModel
    {
        public int Id { get; set; }
        public string EntityName { get; set; } = "Table1";

        public string PkType { get; set; } = "Guid";

        public bool HasApi { get; set; } = true;
        [NotMapped]
        public bool HasAdminPage { get; set; } = false;
        public int? ProjectId { get; set; }

        public bool Applied { get; set; } = false;

        public List<EntityRowModel> Rows { get; set; } = new();

        public virtual ProjectModel? Project { get; set; }

        public virtual List<RelationModel>? ParentRelations { get; set; }
        public virtual List<RelationModel>? ChildRelations { get; set; }
    }
}

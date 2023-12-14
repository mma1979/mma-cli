using Mma.Cli.Shared.Models;

namespace Mma.Cli.UI.Models
{
    public class RelationDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool Applied { get; set; } = false;

        public int? ParentId { get; set; }
        public virtual EntityModel? ParentEntity { get; set; }
        public string? ParentEntityName { get; set; }

        public int? ChildId { get; set; }
        public virtual EntityModel? ChiledEntity { get; set; }
        public string? ChiledEntityName { get; set; }

        public string? ForeignKeyProperty { get; set; }
        public string? PkType { get; set; }


    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Models
{
    public class ProjectModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string? ProjectJson { get; set; }

        public virtual List<EntityModel> Entities { get; set; } = new();
    }
}

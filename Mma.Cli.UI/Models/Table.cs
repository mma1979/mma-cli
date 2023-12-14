using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Mma.Cli.UI.Models
{
    public class Table
    {
        public string Name { get; set; }
        public string SetName
        {
            get
            {
                return Name.ToLower().EndsWith("y") ?
                    $"{Name.TrimEnd('y', 'Y')}ies" :
                        Name.ToLower().EndsWith("s") ?
                        $"{Name}es" :
                        $"{Name}s";
            }
        }
        public string IdType { get; set; }
        

        public List<Column> Columns { get; set; }
        public List<TableRelation> TableRelations { get; set; }
    }
}

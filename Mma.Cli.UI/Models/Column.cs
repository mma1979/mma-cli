namespace Mma.Cli.UI.Models
{
    public class Column
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public bool IsNullable { get; set; }
        public bool IsForeignKey { get; set; }
    }
}

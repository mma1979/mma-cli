namespace Mma.Cli.UI.Models
{
    public class TableRelation
    {
        public string RelatedTableName { get; set; }
        public string SetRelatedTableName
        {
            get
            {
                if (!IsCollection)
                {
                    return RelatedTableName;
                }
                return RelatedTableName.ToLower().EndsWith("y") ?
                    $"{RelatedTableName.TrimEnd('y', 'Y')}ies" :
                        RelatedTableName.ToLower().EndsWith("s") ?
                        $"{RelatedTableName}es" :
                        $"{RelatedTableName}s";
            }
        }
        public string ForeignKey
        {
            get
            {
                return IsCollection ? "" : $"{RelatedTableName}Id";
            }
        }
        public bool IsCollection { get; set; }
    }
}

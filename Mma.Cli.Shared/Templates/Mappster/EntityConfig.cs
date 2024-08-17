using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates.Mappster
{
    public static class MappsterEntityConfig
    {
        public const string Template = @"using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using $SolutionName.Core.Database.Identity;
using $SolutionName.Core.Database.Tables;

namespace $SolutionName.EntityFramework.EntityConfigurations
{
    public class $EntityNameConfig : IEntityTypeConfiguration<$EntityName>
    {
        private readonly string _schema;
        public $EntityNameConfig(string schema = ""dbo"")
        {
            _schema = schema;
        }

       
        public void Configure(EntityTypeBuilder<$EntityName> builder)
        {
            builder.ToTable(""$EntitySetName"", _schema);


            builder.HasQueryFilter(e => e.IsDeleted != true);
            builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValueSql(""((0))"");
            builder.Property(e => e.CreatedDate).IsRequired().HasDefaultValueSql(""(getdate())"");
            builder.HasIndex(e => e.IsDeleted);

            


        }
    }
}";
    }
}

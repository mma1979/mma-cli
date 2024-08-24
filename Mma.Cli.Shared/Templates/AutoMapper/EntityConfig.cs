namespace Mma.Cli.Shared.Templates
{
	public static class EntityConfig
	{
		public const string Template = @"using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using $SolutionName.Core.Database.Identity;
using $SolutionName.Core.Database.Tables;

using $SolutionName.EntityFramework.Infrastrcture;

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
			
			/* // Uncoment if the PK is Guid
			 builder.Property(e => e.Id)
			  .ValueGeneratedOnAdd()
			  .HasValueGenerator<GuidV7ValueGenerator>();
			*/

			 builder.HasQueryFilter(e => e.IsDeleted != true);
			builder.Property(e => e.IsDeleted).IsRequired()
				.HasDefaultValueSql(""((0))"");


			builder.Property(e => e.CreatedDate)
			 .HasColumnType(""datetime"")
			 .ValueGeneratedOnAdd()
			 .HasValueGenerator<CreatedDateTimeValueGenerator>();

			builder.Property(e => e.ModifiedDate)
				.HasColumnType(""datetime"");

			builder.HasIndex(e => e.IsDeleted);
			builder.Property(e => e.DeletedDate)
				.HasColumnType(""datetime"");


		}
	}
}";
	}
}

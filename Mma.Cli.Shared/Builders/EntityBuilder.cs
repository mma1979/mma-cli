using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Helpers;
using Mma.Cli.Shared.Templates;
using Mma.Cli.Shared.Templates.AutoMapper;
using Mma.Cli.Shared.Templates.Mappster;
using Scriban;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Builders
{
    public class EntityBuilder
    {
        public string SolutionPath { get; private set; } = "";
        public string SolutionName { get; private set; } = "";
        public string ProjectsPath { get; private set; } = "";
        public string ComponentType { get; private set; } = "";
        public string ComponentName { get; private set; } = "";
        public string PkType { get; private set; } = "";
        public string Mapper { get; private set; } = "";

        public EntityBuilder()
        {
            SolutionPath = Directory.GetCurrentDirectory();
        }

        public EntityBuilder(string solutionPath)
        {
            SolutionPath = solutionPath;
        }

        public static EntityBuilder New(string mapper, string entityName, string pkTyep)
        {
            var builder = new EntityBuilder
            {
                ComponentType = "Entity",
                ComponentName = entityName,
                PkType = pkTyep,
                Mapper = mapper
            };

            (builder.SolutionName, builder.ProjectsPath) = BuildHelper.CheckSolutionPath(builder.SolutionPath);

            return builder;
        }

        public static EntityBuilder New(string[] args)
        {
            var mapperFlagIndex = Array.IndexOf(args, Flags.MapperFlag);
            var mapper = args[mapperFlagIndex + 1].ToLower() switch
            {
                "mapster" => Mappers.Mapster,
                _ => Mappers.AutoMapper
            };
            var builder = new EntityBuilder();
            builder.ComponentType = args[1];
            builder.ComponentName = args[2];
            builder.PkType = args.Length > 3 ? args[3] : PkTypes.GUID;
            builder.Mapper = mapper;

            (builder.SolutionName, builder.ProjectsPath) = BuildHelper.CheckSolutionPath(builder.SolutionPath);

            return builder;
        }

        public static EntityBuilder New(string[] args, string solutionPath)
        {
            var mapperFlagIndex = Array.IndexOf(args, Flags.MapperFlag);
            var mapper = args[mapperFlagIndex + 1].ToLower() switch
            {
                "mapster" => Mappers.Mapster,
                _ => Mappers.AutoMapper
            };
            var builder = new EntityBuilder(solutionPath);
            builder.ComponentType = args[1];
            builder.ComponentName = args[2];
            builder.PkType = args.Length > 3 ? args[3] : PkTypes.GUID;
            builder.Mapper = mapper;

            (builder.SolutionName, builder.ProjectsPath) = BuildHelper.CheckSolutionPath(builder.SolutionPath);

            return builder;
        }


        public EntityBuilder GenerateModels()
        {
            var model = new { SolutionName, EntityName = ComponentName, PK = PkType };
            var modifyTemplate = Mapper is Mappers.Mapster ? Templates.Mappster.ModifyModel.Template : Templates.AutoMapper.ModifyModel.Template;
            var readTemplate = Mapper is Mappers.Mapster ? Templates.Mappster.ReadModel.Template : Templates.AutoMapper.ReadModel.Template;

            var modifyResult = Template.Parse(modifyTemplate).Render(model);
            var readResult = Template.Parse(readTemplate).Render(model);

            var modifyFileName = Mapper switch
            {
                Mappers.Mapster => $"{ComponentName}ModifyModel.g.cs",
                _ => $"{ComponentName}ModifyModel.cs"
            };
            var readFileName = Mapper switch
            {
                Mappers.Mapster => $"{ComponentName}ReadModel.g.cs",
                _ => $"{ComponentName}ReadModel.cs"
            };

            var modifyPath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", modifyFileName);
            var readPath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", readFileName);

            File.WriteAllText(modifyPath, modifyResult);
            File.WriteAllText(readPath, readResult);

            if (Mapper == Mappers.AutoMapper)
            {
                BuildAutoMapperProfile();
            }

            return this;
        }

        private void BuildAutoMapperProfile()
        {
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "MappingProfile.cs");
            var lines = File.ReadAllLines(path).ToList();
            var last = lines.Last(l => l.EndsWith(";"));
            var idx = lines.IndexOf(last);
            var template = Template.Parse(Templates.AutoMapper.Config.Template);
            var result = template.Render(new { EntityName = ComponentName });
            lines.Insert(idx + 1, result);
            File.WriteAllLines(path, lines);
        }

        public EntityBuilder GenerateValidator()
        {
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Validations", $"{ComponentName}Validator.cs");
            var template = Template.Parse(Validator.Template);
            var result = template.Render(new { SolutionName, EntityName = ComponentName });
            File.WriteAllText(path, result);
            return this;
        }

        public EntityBuilder GenerateEntity()
        {
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Database", "Tables", $"{ComponentName}.cs");
            var templateText = Mapper switch
            {
                Mappers.Mapster => Templates.Mappster.Entity.Template,
                _ => Templates.AutoMapper.Entity.Template
            };
            var template = Template.Parse(templateText);
            var result = template.Render(new { SolutionName, EntityName = ComponentName, PK = PkType });
            File.WriteAllText(path, result);
            return this;
        }

        public EntityBuilder GenerateEntityConfig()
        {
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.EntityFramework", "EntityConfigurations", $"{ComponentName}Config.cs");
            var entitySetName = BuildHelper.GetSetName(ComponentName);
            var templateText = Mapper is Mappers.Mapster ? MappsterEntityConfig.Template : EntityConfig.Template;
            var template = Template.Parse(templateText);
            var result = template.Render(new { SolutionName, EntityName = ComponentName, EntitySetName = entitySetName });
            File.WriteAllText(path, result);
            return this;
        }

        public EntityBuilder DbContextMapping()
        {
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.EntityFramework", "ApplicationDbContext.cs");
            var lines = File.ReadAllLines(path).ToList();
            var entitySetName = BuildHelper.GetSetName(ComponentName);

            var dbSetTemplate = Template.Parse(Templates.DbSetEntry.Template);
            var dbSetResult = dbSetTemplate.Render(new { EntityName = ComponentName, EntitySetName = entitySetName });
            var lastDbSet = lines.Last(l => l.Contains("public virtual DbSet<"));
            var dbSetIndex = lines.IndexOf(lastDbSet);
            lines.Insert(dbSetIndex + 1, dbSetResult);

            var configTemplate = Template.Parse(ConfigEntry.Template);
            var configResult = configTemplate.Render(new { EntityName = ComponentName });
            var lastConfig = lines.Last(l => l.Contains("modelBuilder.ApplyConfiguration(new"));
            var configIndex = lines.IndexOf(lastConfig);
            lines.Insert(configIndex + 1, configResult);

            File.WriteAllLines(path, lines);
            return this;
        }

        public EntityBuilder GenerateService()
        {
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.Services", $"{ComponentName}Service.cs");
            var entitySetName = BuildHelper.GetSetName(ComponentName);
            var entityNameVar = $"{ComponentName[0].ToString().ToLower()}{ComponentName.AsSpan(1).ToString()}";
            var templateText = Mapper switch
            {
                Mappers.Mapster => Templates.Mappster.Service.Template,
                _ => Templates.AutoMapper.Service.Template
            };
            var template = Template.Parse(templateText);
            var result = template.Render(new { SolutionName, EntityName = ComponentName, EntityVarName = entityNameVar, EntitySetName = entitySetName, PK = PkType });
            File.WriteAllText(path, result);
            return this;
        }

        public EntityBuilder GenerateController(bool generate)
        {
            if (!generate) return this;

            var entitySetName = BuildHelper.GetSetName(ComponentName);
            var entityNameVar = $"{ComponentName[0].ToString().ToLower()}{ComponentName.AsSpan(1).ToString()}";
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.AppApi", "Controllers", "v1", $"{entitySetName}Controller.cs");
            var templateText = Mapper is Mappers.Mapster ? MappsterController.Template : Controller.Template;
            var template = Template.Parse(templateText);
            var result = template.Render(new { SolutionName, EntityName = ComponentName, EntityVarName = entityNameVar, EntitySetName = entitySetName, PK = PkType });
            File.WriteAllText(path, result);
            return this;
        }
    }
}
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Helpers;
using Mma.Cli.Shared.Templates;
using Mma.Cli.Shared.Templates.AutoMapper;
using Mma.Cli.Shared.Templates.Mappster;

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
        public string SolutionPath { get; private set; }
        public string SolutionName { get; private set; }
        public string ProjectsPath { get; private set; }
        public string ComponentType { get; private set; }
        public string ComponentName { get; private set; }
        public string PkType { get; private set; }
        public string Mapper { get; private set; }

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


        public EntityBuilder GenerateDto()
        {
            var fileName = Mapper switch
            {
                Mappers.Mapster => $"{ComponentName}Dto.g.cs",
                _ => $"{ComponentName}ModifyModel.cs"
            };
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", fileName);

            using StreamWriter writer = new(path);
            writer.Write(
               (Mapper is Mappers.Mapster? MappsterDtoTemplate.Template : DtoTemplate.Template)
                    .Replace("$SolutionName", SolutionName)
                    .Replace("$EntityName", ComponentName)
                    .Replace("$PK", PkType)
            );
            writer.Flush();
            writer.Close();

            void BuildReadDto()
            {
                var path = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{ComponentName}ReadModel.cs");
                using StreamWriter writer = new(path);
                writer.Write(
                    Templates.AutoMapper.ReadDto.Template
                        .Replace("$SolutionName", SolutionName)
                        .Replace("$EntityName", ComponentName)
                        .Replace("$PK", PkType)
                );
                writer.Flush();
                writer.Close();
            }
            if (Mapper == Mappers.AutoMapper)
            {
                BuildReadDto();
                BuildAutoMapperProfile();
            }


            return this;
        }

        private void BuildAutoMapperProfile()
        {
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "MappingProfile.cs");
            List<string> ReadLines()
            {

                using StreamReader sr = new(path);
                var lines = new List<string>();
                while (true)
                {
                    var line = sr.ReadLine();
                    if (line is null) break;
                    lines.Add(line);
                }
                sr.Close();

                return lines;
            }

            void InsertAutoMapperConfig(List<string> ls)
            {
                var last = ls.Last(l => l.EndsWith(";"));
                var idx = ls.IndexOf(last);
                ls.Insert(idx + 1,
                    Templates.AutoMapper.Config.Template
                        .Replace("$EntityName", ComponentName));
            }

            void RewriteAutoMapperProfile(List<string> ls)
            {


                var content = string.Join('\n', ls);
                using StreamWriter sw = new(path);
                sw.Write(content);
                sw.Flush();
                sw.Close();
            }

            var lines = ReadLines();
            InsertAutoMapperConfig(lines);
            RewriteAutoMapperProfile(lines);


        }

        public EntityBuilder GenerateValidator()
        {
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Validations", $"{ComponentName}Validator.cs");

            using StreamWriter writer = new(path);
            writer.Write(
                (Mapper is Mappers.Mapster? MapsterValidator.Template : Validator.Template)
                    .Replace("$SolutionName", SolutionName)
                    .Replace("$EntityName", ComponentName)
            );
            writer.Flush();
            writer.Close();

            return this;
        }

        public EntityBuilder GenerateEntity()
        {
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Database", "Tables", $"{ComponentName}.cs");

            var template = Mapper switch
            {
                Mappers.Mapster => Templates.Mappster.Entity.Template,
                _ => Templates.AutoMapper.Entity.Template
            };

            using StreamWriter writer = new(path);
            writer.Write(
                template
                    .Replace("$SolutionName", SolutionName)
                    .Replace("$EntityName", ComponentName)
                    .Replace("$PK", PkType)
            );
            writer.Flush();
            writer.Close();

            return this;
        }

        public EntityBuilder GenerateEntityConfig()
        {

            var path = Path.Combine(ProjectsPath, $"{SolutionName}.EntityFramework", "EntityConfigurations", $"{ComponentName}Config.cs");

            var entitySetName = BuildHelper.GetSetName(ComponentName);

            using StreamWriter writer = new(path);
            writer.Write(
                (Mapper is Mappers.Mapster? MappsterEntityConfig.Template : EntityConfig.Template)
                    .Replace("$SolutionName", SolutionName)
                    .Replace("$EntityName", ComponentName)
                    .Replace("$EntitySetName", entitySetName)
            );
            writer.Flush();
            writer.Close();

            return this;
        }
               

        public EntityBuilder DbContextMapping()
        {
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.EntityFramework", "ApplicationDbContext.cs");

            List<string> ReadLines()
            {

                using StreamReader sr = new(path);
                var lines = new List<string>();
                while (true)
                {
                    var line = sr.ReadLine();
                    if (line is null) break;
                    lines.Add(line);
                }
                sr.Close();

                return lines;
            }

            void InsertDbSetEntry(List<string> ls, string setName)
            {
                var last = ls.Last(l => l.Contains("public virtual DbSet<"));
                var idx = ls.IndexOf(last);
                ls.Insert(idx + 1,
                    Templates.DbSetEntry.Template
                        .Replace("$EntityName", ComponentName)
                        .Replace("$EntitySetName", setName));
            }

            void InsertConfigEntry(List<string> ls)
            {
                var last = ls.Last(l => l.Contains("modelBuilder.ApplyConfiguration(new"));
                var idx = ls.IndexOf(last);
                ls.Insert(idx + 1,
                    ConfigEntry.Template
                        .Replace("$EntityName", ComponentName));
            }

            void RewriteApplicationDbContext(List<string> ls)
            {
                var content = string.Join('\n', ls);
                using StreamWriter sw = new(path);
                sw.Write(content);
                sw.Flush();
                sw.Close();
            }

            var entitySetName = BuildHelper.GetSetName(ComponentName);
            var lines = ReadLines();
            InsertDbSetEntry(lines, entitySetName);
            InsertConfigEntry(lines);
            RewriteApplicationDbContext(lines);


            return this;
        }

        public EntityBuilder GenerateService()
        {

            var path = Path.Combine(ProjectsPath, $"{SolutionName}.Services", $"{ComponentName}Service.cs");

            var entitySetName = BuildHelper.GetSetName(ComponentName);
            var entityNameVar = $"{ComponentName[0].ToString().ToLower()}{ComponentName.AsSpan(1).ToString()}";

            var template = Mapper switch
            {
                Mappers.Mapster => Templates.Mappster.Service.Template,
                _ => Templates.AutoMapper.Service.Template
            };

            using StreamWriter writer = new(path);
            writer.Write(
                template
                    .Replace("$SolutionName", SolutionName)
                    .Replace("$EntityName", ComponentName)
                    .Replace("$EntityVarName", entityNameVar)
                    .Replace("$EntitySetName", entitySetName)
                    .Replace("$PK", PkType)
            );
            writer.Flush();
            writer.Close();

            return this;
        }

        public EntityBuilder GenerateController(bool generate)
        {
            if (!generate) return this;

            var entitySetName = BuildHelper.GetSetName(ComponentName);
            var entityNameVar = $"{ComponentName[0].ToString().ToLower()}{ComponentName.AsSpan(1).ToString()}";

            var path = Path.Combine(ProjectsPath, $"{SolutionName}.AppApi", "Controllers", "v1", $"{entitySetName}Controller.cs");


            using StreamWriter writer = new(path);
            writer.Write(
               (Mapper is Mappers.Mapster? MappsterController.Template:Controller.Template)
               .Replace("$SolutionName", SolutionName)
                    .Replace("$EntityName", ComponentName)
                    .Replace("$EntityVarName", entityNameVar)
                    .Replace("$EntitySetName", entitySetName)
                    .Replace("$PK", PkType)
            );
            writer.Flush();
            writer.Close();

            return this;
        }
    }
}

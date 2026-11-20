using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Builders
{
    public class RelationsBuilder
    {
        public string SolutionPath { get; private set; } = "";
        public string SolutionName { get; private set; } = "";
        public string ProjectsPath { get; private set; } = "";
        public string Mapper { get; private set; } = "";

        public string ParentEntity { get; set; } = "";
        public string ChiledEntity { get; set; } = "";
        public string ForeignKey { get; set; } = "";
        public string ForeignDataType { get; set; } = "";
        public bool RemoveFlag { get; set; }

        public RelationsBuilder() => SolutionPath = Directory.GetCurrentDirectory();

        public RelationsBuilder(string solutionPath)
        {
            SolutionPath = solutionPath;
        }  

        public static RelationsBuilder New(string[] args) => Initialize(new RelationsBuilder(), args);

        public static RelationsBuilder New(string[] args, string solutionPath) => Initialize(new RelationsBuilder(solutionPath), args);

        private static RelationsBuilder Initialize(RelationsBuilder builder, string[] args)
        {
            if (args.Length < 5)
            {
                Output.Error("Not enough arguments use mma -h to get help");
                Environment.Exit(-1);
                return null;
            }

            builder.ParentEntity = args[2];
            builder.ChiledEntity = args[3];
            builder.ForeignKey = args[4];
            builder.ForeignDataType = args.Length > 5 && !args[5].StartsWith("--", StringComparison.Ordinal)
                ? args[5]
                : PkTypes.GUID;
            builder.RemoveFlag = args.Contains(Flags.RemoveFlag);

            (builder.SolutionName, builder.ProjectsPath) = BuildHelper.CheckSolutionPath(builder.SolutionPath);
            builder.Mapper = BuildHelper.DetectMapper(builder.SolutionPath);

            return builder;
        }

        public RelationsBuilder UpdateParentDtos()
        {

            var navigation = BuildProperty(false, true, $"List<{ChiledEntity}>", BuildHelper.GetSetName(ChiledEntity));
            var modelSuffix = Mapper == Mappers.Mapster ? ".g.cs" : ".cs";

            void UpdateModifiyDto()
            {
                var dtoFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{ParentEntity}ModifyModel{modelSuffix}");

                var lines = File.ReadAllLines(dtoFilePath).ToList();
                var index = lines.IndexOf(lines[^2]);

                lines.Insert(index, navigation);

                using StreamWriter writer = new(dtoFilePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            void RemoveFromModifiyDto()
            {
                var dtoFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{ParentEntity}ModifyModel{modelSuffix}");

                var lines = File.ReadAllLines(dtoFilePath).ToList();
                var navigation_index = lines.FindIndex(l => l.Contains(navigation));

                if (navigation_index != -1)
                {
                    lines.RemoveAt(navigation_index);
                }

                using StreamWriter writer = new(dtoFilePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            if (RemoveFlag)
            {
                RemoveFromModifiyDto();
            }
            else
            {
                UpdateModifiyDto();
            }
            return this;
        }
        public RelationsBuilder UpdateChildDtos()
        {
            var navigation = BuildProperty(false, true, ParentEntity, ParentEntity);

            var foreignKey = BuildProperty(false, false, ForeignDataType, ForeignKey);
            var modelSuffix = Mapper == Mappers.Mapster ? ".g.cs" : ".cs";

            void UpdateModifiyDto()
            {
                var dtoFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{ChiledEntity}ModifyModel{modelSuffix}");

                var lines = File.ReadAllLines(dtoFilePath).ToList();
                var index = lines.IndexOf(lines[^2]);

                lines.Insert(index, navigation);
                lines.Insert(index, foreignKey);

                using StreamWriter writer = new(dtoFilePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void UpdateReadDto()
            {

                var readDtoFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{ChiledEntity}ReadModel{modelSuffix}");

                var lines = File.ReadAllLines(readDtoFilePath).ToList();
                var index = lines.IndexOf(lines[^2]);

                lines.Insert(index, foreignKey);

                using StreamWriter writer = new(readDtoFilePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            void RemoveFromModifiyDto()
            {
                var dtoFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{ChiledEntity}ModifyModel{modelSuffix}");

                var lines = File.ReadAllLines(dtoFilePath).ToList();
                var index = lines.FindIndex(l => l.Contains(foreignKey));

                if (index != -1)
                {
                    lines.RemoveRange(index, 2);
                }

                using StreamWriter writer = new(dtoFilePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void RemoveFromReadDto()
            {

                var readDtoFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{ChiledEntity}ReadModel{modelSuffix}");

                var lines = File.ReadAllLines(readDtoFilePath).ToList();
                var index = lines.FindIndex(l => l.Contains(foreignKey));

                if (index != -1)
                {
                    lines.RemoveAt(index);
                }

                using StreamWriter writer = new(readDtoFilePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }


            if (RemoveFlag)
            {
                RemoveFromModifiyDto();
                RemoveFromReadDto();
            }
            else
            {
                UpdateModifiyDto();
                UpdateReadDto();
            }
            return this;
        }

        public RelationsBuilder UpdateParentEntity()
        {
            var children = BuildHelper.GetSetName(ChiledEntity);
            var navigation = BuildProperty(false, true, $"ICollection<{ChiledEntity}>", children);

            var filePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Database", "Tables", $"{ParentEntity}.cs");

            var assignation = $"{children} ??= new List<{ChiledEntity}>();";

            void UpdateProps()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains($"{ParentEntity}Validator _Validator;")) - 1;

                if (index >= 0)
                {
                    lines.Insert(index, navigation);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void UpdatePublicConstractor()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains($"public {ParentEntity}("));

                if (index != -1)
                {
                    lines.Insert(index + 10, assignation);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void UpdateFunction()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains($"public {ParentEntity} Update("));

                if (index != -1)
                {
                    lines.Insert(index + 10, assignation);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            void RemoveProps()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains(navigation));

                if (index != -1)
                {
                    lines.RemoveAt(index);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void RemovePublicConstractor()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains(assignation));

                if (index != -1)
                {
                    lines.RemoveAt(index);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void RemoveFunction()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains(assignation));

                if (index != -1)
                {
                    lines.RemoveAt(index);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            if (RemoveFlag)
            {
                RemoveProps();
                RemovePublicConstractor();
                RemoveFunction();
            }
            else
            {
                UpdateProps();
                UpdatePublicConstractor();
                UpdateFunction();
            }

            return this;
        }

        public RelationsBuilder UpdateChildEntity()
        {
            var navigation = BuildProperty(true, true, ParentEntity, ParentEntity);

            var foreignKey = BuildProperty(true, false, ForeignDataType, ForeignKey);

            var filePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Database", "Tables", $"{ChiledEntity}.cs");

            var assignation = $"{ForeignKey} = dto.{ForeignKey};";

            void UpdateProperties()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var line = lines.First(l => l.Contains($"{ChiledEntity}Validator _Validator;"));
                var index = lines.IndexOf(line) - 1;

                lines.Insert(index, navigation);
                lines.Insert(index, foreignKey);

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void UpdatePublicConstractor()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains($"public {ChiledEntity}("));

                if (index != -1)
                {
                    lines.Insert(index + 10, assignation);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void UpdateFunction()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains($"public {ChiledEntity} Update("));

                if (index != -1)
                {
                    lines.Insert(index + 10, assignation);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }


            void RemoveProperties()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains(foreignKey));

                if (index != -1)
                {
                    lines.RemoveRange(index, 2);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void RemovePublicConstractor()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains(assignation));

                if (index != -1)
                {
                    lines.RemoveAt(index);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void RemoveFunction()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains(assignation));

                if (index != -1)
                {
                    lines.RemoveAt(index);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            if (RemoveFlag)
            {
                RemoveProperties();
                RemovePublicConstractor();
                RemoveFunction();
            }
            else
            {
                UpdateProperties();
                UpdatePublicConstractor();
                UpdateFunction();
            }
            return this;
        }

        public RelationsBuilder UpdateParentEntityConfig()
        {
            var path = Path.Combine(ProjectsPath, $"{SolutionName}.EntityFramework", "EntityConfigurations", $"{ParentEntity}Config.cs");

            var config = $"builder.HasMany(e => e.{BuildHelper.GetSetName(ChiledEntity)}).WithOne(e => e.{ParentEntity}).HasForeignKey(e => e.{ForeignKey}).OnDelete(DeleteBehavior.Cascade);";

            void UpdateConfig()
            {
                var lines = File.ReadAllLines(path).ToList();
                var line = lines.First(l => l.Contains("builder.HasIndex(e => e.IsDeleted);"));
                var index = lines.IndexOf(line) + 2;

                lines.Insert(index, config);

                using StreamWriter writer = new(path);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            void RemoveConfig()
            {
                var lines = File.ReadAllLines(path).ToList();
                var index = lines.FindIndex(l => l.Contains(config));

                if (index != -1)
                {
                    lines.RemoveAt(index);
                }

                using StreamWriter writer = new(path);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            if (RemoveFlag)
                RemoveConfig();
            else
                UpdateConfig();
            return this;
        }

      

        private string BuildProperty(bool isPrivateSet, bool isVirtual, string type, string name)
        {
            var modifier = isPrivateSet ? "private " : "";
            var virtualized = isVirtual ? "virtual " : "";

            return $"public {virtualized} {type}? {name} {{get; {modifier}set;}}";
        }

       
    }
}

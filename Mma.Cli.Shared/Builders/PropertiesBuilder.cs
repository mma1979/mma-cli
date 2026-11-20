using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Builders
{
    public class PropertiesBuilder
    {
        public string SolutionPath { get; private set; } = "";
        public string SolutionName { get; private set; } = "";
        public string ProjectsPath { get; private set; } = "";
        public string Mapper { get; private set; } = "";

        public string EntityName { get; set; } = "";
        public string PropertyName { get; set; } = "";
        public string DataType { get; set; } = "";
        public bool Nullable { get; set; }
        public bool RemoveFlag { get; set; }

        public PropertiesBuilder()
        {
            SolutionPath = Directory.GetCurrentDirectory();
        }

        public PropertiesBuilder(string solutionPath)
        {
            SolutionPath = solutionPath;
        }

        public static PropertiesBuilder New(string[] args, string mapper)
        {

            if (args.Length < 6)
            {
                Output.Error("Not enough arguments use mma -h to get help");
                Environment.Exit(-1);
                return null;
            }

            var builder = new PropertiesBuilder
            {
                Mapper = mapper,
                EntityName = args[2],
                PropertyName = args[3],
                DataType = args[4],
                Nullable = args[5].ToLower() == "true",
                RemoveFlag = args.Contains(Flags.RemoveFlag),
            };

            (builder.SolutionName, builder.ProjectsPath) = BuildHelper.CheckSolutionPath(builder.SolutionPath);


            return builder;
        }

        public static PropertiesBuilder New(string[] args, string mapper, string solutionPath)
        {

            if (args.Length < 6)
            {
                Output.Error("Not enough arguments use mma -h to get help");
                Environment.Exit(-1);
                return null;
            }

            var builder = new PropertiesBuilder(solutionPath)
            {
                Mapper = mapper,
                EntityName = args[2],
                PropertyName = args[3],
                DataType = args[4],
                Nullable = args[5].ToLower() == "true",
                RemoveFlag = args.Contains(Flags.RemoveFlag),
            };

            (builder.SolutionName, builder.ProjectsPath) = BuildHelper.CheckSolutionPath(builder.SolutionPath);


            return builder;
        }

        public PropertiesBuilder UpdateEntityModels()
        {
            var property = BuildProperty(false);
            var modelSuffix = Mapper == Mappers.Mapster ? ".g.cs" : ".cs";

            void UpdateModifyModel()
            {
                var modelFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{EntityName}ModifyModel{modelSuffix}");

                var lines = File.ReadAllLines(modelFilePath).ToList();
                var index = lines.IndexOf(lines[^2]);

                lines.Insert(index, property);

                using StreamWriter writer = new(modelFilePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            void UpdateReadModel()
            {

                var modelFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{EntityName}ReadModel{modelSuffix}");

                var lines = File.ReadAllLines(modelFilePath).ToList();
                var index = lines.IndexOf(lines[^2]);

                lines.Insert(index, property);

                using StreamWriter writer = new(modelFilePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            void RemoveFromModifiyModel()
            {
                var modelFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{EntityName}ModifyModel{modelSuffix}");

                var lines = File.ReadAllLines(modelFilePath).ToList();
                var index = lines.FindIndex(l => l.Contains(property));

                if (index != -1)
                {
                    lines.RemoveAt(index);
                }

                using StreamWriter writer = new(modelFilePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            void RemoveFromReadModel()
            {

                var readDtoFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{EntityName}ReadModel{modelSuffix}");

                var lines = File.ReadAllLines(readDtoFilePath).ToList();
                var index = lines.FindIndex(l => l.Contains(property));

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
                RemoveFromModifiyModel();
                RemoveFromReadModel();
            }
            else
            {
                UpdateModifyModel();
                UpdateReadModel();
            }


            return this;
        }

        public PropertiesBuilder UpdateEntity()
        {
            var property = BuildProperty(true);
            var filePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Database", "Tables", $"{EntityName}.cs");

            var assignation = $"{PropertyName} = dto.{PropertyName};";

            void UpdateProps()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains($"{EntityName}Validator _Validator;")) - 1;

                if (index >= 0)
                {
                    lines.Insert(index, property);
                }

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void UpdatePublicConstractor()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var index = lines.FindIndex(l => l.Contains($"public {EntityName}("));

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
                var index = lines.FindIndex(l => l.Contains($"public {EntityName} Update("));

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
                var index = lines.FindIndex(l => l.Contains(property));

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

        public PropertiesBuilder UpdateEntityConfig()
        {
            var config = DataType switch
            {
                PkTypes.STRING => $"builder.Property(e => e.{PropertyName}).HasMaxLength(255);",
                PkTypes.DECIMAL => $"builder.Property(e => e.{PropertyName}).HasColumnType(\"decimal(9,3)\");",
                PkTypes.DATE_TIME => $"builder.Property(e => e.{PropertyName}).HasColumnType(\"datetime\");",
                PkTypes.GUID => $"builder.Property(e => e.{PropertyName}).HasDefaultValue(\"newid()\");",
                PkTypes.BYTE_ARRAY => $"builder.Property(e => e.{PropertyName}).HasColumnType(\"varbinary(max)\");",
                PkTypes.TIME_SPAN => $"builder.Property(e => e.{PropertyName}).HasColumnType(\"time\");",
                _ => ""
            };

            if (string.IsNullOrEmpty(config))
            {
                return this;
            }

            var path = Path.Combine(ProjectsPath, $"{SolutionName}.EntityFramework", "EntityConfigurations", $"{EntityName}Config.cs");

            void UpdateConfig()
            {
                if (string.IsNullOrEmpty(config)) return;

                var lines = File.ReadAllLines(path).ToList();
                var index = lines.FindIndex(l => l.Contains("builder.HasIndex(e => e.IsDeleted);")) + 2;

                if (index > 1)
                {
                    lines.Insert(index, config);
                }

                using StreamWriter writer = new(path);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            void RemoveConfig()
            {
                if (string.IsNullOrEmpty(config)) return;

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

       
        private string BuildProperty(bool isPrivateSet)
        {
            var modifier = isPrivateSet ? "private " : "";
            string nullable = Nullable ? "?" : "";

            return $"public {DataType}{nullable} {PropertyName} {{get; {modifier}set;}}";
        }

        
    }
}

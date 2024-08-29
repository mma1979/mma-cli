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
        public string SolutionPath { get; private set; }
        public string SolutionName { get; private set; }
        public string ProjectsPath { get; private set; }
        public string Mapper { get; private set; }

        public string EntityName { get; set; }
        public string PropertyName { get; set; }
        public string DataType { get; set; }
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

            void UpdateModifyModel()
            {
                var modelFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{EntityName}ModifyModel.cs");

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

                var modelFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{EntityName}ReadModel.cs");

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
                var modelFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{EntityName}ModifyModel.cs");

                var lines = File.ReadAllLines(modelFilePath).ToList();
                var line = lines.FirstOrDefault(l => l.Contains(property));
                var index = lines.IndexOf(line);

                lines.RemoveAt(index);

                using StreamWriter writer = new(modelFilePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            void RemoveFromReadModel()
            {

                var readDtoFilePath = Path.Combine(ProjectsPath, $"{SolutionName}.Core", "Models", $"{EntityName}ReadModel.cs");

                var lines = File.ReadAllLines(readDtoFilePath).ToList();
                var line = lines.FirstOrDefault(l => l.Contains(property));
                var index = lines.IndexOf(line);

                lines.RemoveAt(index);

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
                var line = lines.First(l => l.Contains($"{EntityName}Validator _Validator;"));
                var index = lines.IndexOf(line) - 1;

                lines.Insert(index, property);

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void UpdatePublicConstractor()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var line = lines.First(l => l.Contains($"public {EntityName}("));
                var index = lines.IndexOf(line) + 10;

                lines.Insert(index, assignation);

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void UpdateFunction()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var line = lines.First(l => l.Contains($"public {EntityName} Update("));
                var index = lines.IndexOf(line) + 10;

                lines.Insert(index, assignation);

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            void RemoveProps()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var line = lines.First(l => l.Contains(property));
                var index = lines.IndexOf(line);

                lines.RemoveAt(index);

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void RemovePublicConstractor()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var line = lines.First(l => l.Contains(assignation));
                var index = lines.IndexOf(line);

                lines.RemoveAt(index);

                using StreamWriter writer = new(filePath);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }
            void RemoveFunction()
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var line = lines.First(l => l.Contains(assignation));
                var index = lines.IndexOf(line);

                lines.RemoveAt(index);

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

            var path = Path.Combine(ProjectsPath, $"{SolutionName}.EntityFramework", "EntityConfigurations", $"{EntityName}Config.cs");

            void UpdateConfig()
            {
                var lines = File.ReadAllLines(path).ToList();
                var line = lines.First(l => l.Contains("builder.HasIndex(e => e.IsDeleted);"));
                var index = lines.IndexOf(line) + 2;

                var config = DataType switch
                {
                    PkTypes.STRING => $"builder.Property(e => e.{PropertyName}).HasMaxLength(255);",
                    PkTypes.DECIMAL => $"builder.Property(e => e.{PropertyName}).HasColumnType(\"decimal(9,3)\");",
                    PkTypes.DATE_TIME => $"builder.Property(e => e.{PropertyName}).HasColumnType(\"datetime\");",
                    PkTypes.GUID => $"builder.Property(e => e.{PropertyName}).HasDefaultValue(\"newid()\");",
                    _ => ""

                };

                lines.Insert(index, config);

                using StreamWriter writer = new(path);
                writer.Write(string.Join('\n', lines));

                writer.Flush();
                writer.Close();
            }

            void RemoveConfig()
            {
                var lines = File.ReadAllLines(path).ToList();

                var config = DataType switch
                {
                    PkTypes.STRING => $"builder.Property(e => e.{PropertyName}).HasMaxLength(255);",
                    PkTypes.DECIMAL => $"builder.Property(e => e.{PropertyName}).HasColumnType(\"decimal(9,3)\");",
                    PkTypes.DATE_TIME => $"builder.Property(e => e.{PropertyName}).HasColumnType(\"datetime\");",
                    PkTypes.GUID => $"builder.Property(e => e.{PropertyName}).HasDefaultValue(\"newid()\");",
                    _ => ""

                };

                var line = lines.First(l => l.Contains(config));
                var index = lines.IndexOf(line);

                lines.RemoveAt(index);

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

using ICSharpCode.SharpZipLib.Zip;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Helpers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Builders
{
    public class SolutionBuilder
    {
        public string SolutionName { get; private set; } = "";
        public string SolutionPath { get; private set; } = "";
        public string ProjectsPath { get; private set; } = "";
        public string Mapper { get; private set; } = "";
        private string _templateSolutionName;

        public SolutionBuilder(string solutionName)
        {
            SolutionName = solutionName;
        }

        public static SolutionBuilder New(string solutionName, string mapper)
        {
            SolutionBuilder sb = new(solutionName) { Mapper = mapper };
            sb._templateSolutionName = mapper == Mappers.Mapster ? "AxialSystem.Covaluse" : "MySolutionName";
            return sb;
        }

        public static SolutionBuilder New(string[] args)
        {
            var solutionName = args[1];
            var mapperFlagIndex = Array.IndexOf(args, Flags.MapperFlag);
            var mapper = args.Length > mapperFlagIndex + 1 && args[mapperFlagIndex + 1].ToLower() == "mapster" ? Mappers.Mapster : Mappers.AutoMapper;
            SolutionBuilder sb = new(solutionName) { Mapper = mapper };
            sb._templateSolutionName = mapper == Mappers.Mapster ? "AxialSystem.Covaluse" : "MySolutionName";
            return sb;
        }

        public SolutionBuilder CreateSolutionDirectory()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), SolutionName);
            Output.Warning($"Creating {path}");
            Directory.CreateDirectory(path);
            SolutionPath = path;
            return this;
        }

        public SolutionBuilder ExtractSolution()
        {
            var zipFile = Mapper switch
            {
                Mappers.Mapster => Resources.Solutions.Mappster,
                _ => Resources.Solutions.AutoMapper
            };
            var path = Path.Combine(SolutionPath, "solution.zip");
            File.WriteAllBytes(path, zipFile);

            var fz = new FastZip();
            fz.ExtractZip(path, SolutionPath, "");

            File.Delete(path);
            return this;
        }

        public SolutionBuilder RootRenameAndReplace()
        {
            var slnPath = Path.Combine(SolutionPath, $"{_templateSolutionName}.sln");
            var newSlnPath = Path.Combine(SolutionPath, $"{SolutionName}.sln");
            ReplaceContent(slnPath, newSlnPath);
            File.Delete(slnPath);

            var projectsPath = Path.Combine(SolutionPath, _templateSolutionName);
            var newProjectsPath = Path.Combine(SolutionPath, SolutionName);
            Directory.Move(projectsPath, newProjectsPath);
            ProjectsPath = newProjectsPath;

            return this;
        }

        public SolutionBuilder RenameProjects()
        {
            var dirs = Directory.GetDirectories(ProjectsPath);
            Parallel.ForEach(dirs, d => Directory.Move(d, d.Replace(_templateSolutionName, SolutionName)));
            return this;
        }

        public SolutionBuilder RenameCsprojFiles()
        {
            string[] files = Directory.GetFiles(ProjectsPath, "*.csproj", SearchOption.AllDirectories);
            Parallel.ForEach(files, f => File.Move(f, f.Replace(_templateSolutionName, SolutionName)));
            return this;
        }

        public SolutionBuilder ReplaceNamespaces()
        {
            string[] files = Directory.GetFiles(ProjectsPath, "*.*", SearchOption.AllDirectories)
                .Where(file => Regex.IsMatch(file, @"^.+\.(cs|json|csproj|cshtml)$")).ToArray();

            Parallel.ForEach(files, f => ReplaceContent(f, f));
            return this;
        }

        private void ReplaceContent(string sourcePath, string destinationPath)
        {
            var content = File.ReadAllText(sourcePath);
            var newContent = content
                .Replace(_templateSolutionName, SolutionName)
                .Replace(_templateSolutionName.ToLower().Replace(".", "-"), SolutionName.ToLower().Replace(".", "-"));

            File.WriteAllText(destinationPath, newContent);
        }

        public SolutionBuilder CreateMmaFolder()
        {
            var mmaDir = Directory.CreateDirectory(Path.Combine(SolutionPath, ".mma"));
            mmaDir.Attributes = FileAttributes.Directory | FileAttributes.Hidden;

            var p = new
            {
                Project = new
                {
                    Name = SolutionName,
                    Path = SolutionPath
                },
                Entities = new List<object>(),
                Rows = new List<object>(),
                Relations = new List<object>()
            };

            using StreamWriter writer = new(Path.Combine(mmaDir.FullName, "project.mma"), false, Encoding.UTF8);
            writer.Write(JsonConvert.SerializeObject(p));
            return this;
        }
    }
}
using ICSharpCode.SharpZipLib.Zip;

using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Helpers;
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Resources;
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

        public SolutionBuilder(string solutionName)
        {
            SolutionName = solutionName;
        }

        public static SolutionBuilder New(string solutionName, string mapper)
        {
            SolutionBuilder sb = new(solutionName) { Mapper = mapper };
            return sb;
        }

        public static SolutionBuilder New(string[] args)
        {
            var solutionName = args[1];
            var flags = args.Where(a => a.StartsWith("--"));
            var mapperFlagIndex = Array.IndexOf(args, Flags.MapperFlag);
            var mapper = args[mapperFlagIndex + 1].ToLower() switch
            {
                "mapster" => Mappers.Mapster,
                _ => Mappers.AutoMapper
            };
            SolutionBuilder sb = new(solutionName) { Mapper = mapper };
            return sb;
        }

        public SolutionBuilder CreateSolutionDirectory()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), SolutionName);
            Output.Warning($"Creating {path}");
            _ = Directory.CreateDirectory(path);
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
            using FileStream wfs = new(path, FileMode.Create, FileAccess.Write);
            wfs.Write(zipFile, 0, zipFile.Length);
            wfs.Flush();
            wfs.Close();

            var fz = new FastZip();
            fz.ExtractZip(path, SolutionPath, "");

            File.Delete(path);
            return this;
        }

        public SolutionBuilder RootRenameAndReplace()
        {
            return Mapper switch
            {
                Mappers.Mapster => MappsterRootRenameAndReplace(),
                _ => AutoMapperRootRenameAndReplace()
            };

        }

        private SolutionBuilder MappsterRootRenameAndReplace()
        {
            var sln = Path.Combine(SolutionPath, $"AxialSystem.Covaluse.sln");
            using FileStream fs = new(sln, FileMode.Open, FileAccess.Read);
            using StreamReader sr = new(fs);
            var content = sr.ReadToEnd();

            fs.Flush();
            fs.Close();
            sr.Close();

            using StreamWriter sw = new(sln.Replace("AxialSystem.Covaluse", SolutionName));
            sw.Write(content.Replace("AxialSystem.Covaluse", SolutionName));
            sw.Flush();
            sw.Close();

            File.Delete(sln);

            var projectsPath = Path.Combine(SolutionPath, "AxialSystem.Covaluse");
            Directory.Move(projectsPath, projectsPath.Replace("AxialSystem.Covaluse", SolutionName));

            ProjectsPath = projectsPath.Replace("AxialSystem.Covaluse", SolutionName);

            return this;

        }

        private SolutionBuilder AutoMapperRootRenameAndReplace()
        {
            var sln = Path.Combine(SolutionPath, $"MySolutionName.sln");
            using FileStream fs = new(sln, FileMode.Open, FileAccess.Read);
            using StreamReader sr = new(fs);
            var content = sr.ReadToEnd();

            fs.Flush();
            fs.Close();
            sr.Close();

            using StreamWriter sw = new(sln.Replace("MySolutionName", SolutionName));
            sw.Write(content.Replace("MySolutionName", SolutionName));
            sw.Flush();
            sw.Close();

            File.Delete(sln);

            var projectsPath = Path.Combine(SolutionPath, "MySolutionName");
            Directory.Move(projectsPath, projectsPath.Replace("MySolutionName", SolutionName));

            ProjectsPath = projectsPath.Replace("MySolutionName", SolutionName);

            return this;

        }

        public SolutionBuilder RenameProjects()
        {
            return Mapper switch
            {
                Mappers.Mapster => MappsterRenameProjects(),
                _ => AutoMapperRenameProjects()
            };
        }

        private SolutionBuilder MappsterRenameProjects()
        {
            var dirs = Directory.GetDirectories(ProjectsPath);
            Parallel.ForEach(dirs, d => Directory.Move(d, d.Replace("AxialSystem.Covaluse", SolutionName)));

            return this;
        }

        private SolutionBuilder AutoMapperRenameProjects()
        {
            var dirs = Directory.GetDirectories(ProjectsPath);
            Parallel.ForEach(dirs, d => Directory.Move(d, d.Replace("MySolutionName", SolutionName)));

            return this;
        }

        public SolutionBuilder RenameCsprojFiles()
        {
            return Mapper switch
            {
                Mappers.Mapster => MappsterRenameCsprojFiles(),
                _ => AutoMapperRenameCsprojFiles()
            };
        }

        private SolutionBuilder MappsterRenameCsprojFiles()
        {
            string[] files = Directory.GetFiles(ProjectsPath, "*.csproj", SearchOption.AllDirectories);
            Parallel.ForEach(files, f => File.Move(f, f.Replace("AxialSystem.Covaluse", SolutionName)));

            return this;
        }

        private SolutionBuilder AutoMapperRenameCsprojFiles()
        {
            string[] files = Directory.GetFiles(ProjectsPath, "*.csproj", SearchOption.AllDirectories);
            Parallel.ForEach(files, f => File.Move(f, f.Replace("MySolutionName", SolutionName)));

            return this;
        }

        public SolutionBuilder ReplaceNamespaces()
        {
            string[] files = Directory.GetFiles(ProjectsPath, "*.*", SearchOption.AllDirectories)
                .Where(file => Regex.IsMatch(file, @"^.+\.(cs|json|csproj|cshtml)$")).ToArray();

            Parallel.ForEach(files, ReplaceContent);
            return this;

        }

        private void ReplaceContent(string fPath)
        {
            using FileStream fs = new(fPath, FileMode.Open, FileAccess.Read);
            using StreamReader sr = new(fs);
            var content = sr.ReadToEnd();

            fs.Flush();
            fs.Close();
            sr.Close();

            File.Delete(fPath);

            using StreamWriter sw = new(fPath);
            sw.Write(content
                .Replace("AxialSystem.Covaluse", SolutionName)
                .Replace("MySolutionName", SolutionName)
                .Replace("axialaystem-covaluse", SolutionName.ToLower().Replace(".", "-"))
                .Replace("mysolutionname", SolutionName.ToLower().Replace(".", "-")));
            sw.Flush();
            sw.Close();
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
            writer.Flush();
            writer.Close();

            return this;

        }

    }
}


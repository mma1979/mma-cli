using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Exceptions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Helpers
{
    public static class BuildHelper
    {
        public static string DetectMapper()
        {
            var solutionPath = Directory.GetCurrentDirectory();

            var files = Directory.GetFiles(solutionPath, "MappingProfile.cs", SearchOption.AllDirectories);

            return files.Any() ?
                Mappers.AutoMapper :
                Mappers.Mapster;

        }

        public static string DetectMapper(string solutionPath)
        {
            var files = Directory.GetFiles(solutionPath, "MappingProfile.cs", SearchOption.AllDirectories);

            return files.Any() ?
                Mappers.AutoMapper :
                Mappers.Mapster;

        }

        public static string GetExecutablePath()
        {
            string executablePath = Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(executablePath)!;
        }

        public static string GetSetName(string componentName) =>
           componentName.EndsWith("s") ? $"{componentName}es" :
           componentName.EndsWith("y") ? $"{componentName.TrimEnd('y')}ies" :
           $"{componentName}s";

        public static (string solutionName, string projectsPath) CheckSolutionPath(string solutionPath)
        {
            if (string.IsNullOrWhiteSpace(solutionPath) || !Directory.Exists(solutionPath))
            {
                throw new MmaCliException(
                    "invalid_solution_path",
                    $"The directory '{solutionPath}' does not exist.",
                    exitCode: 66);
            }

            var sln = Directory.GetFiles(solutionPath, "*.sln", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(solutionPath, "*.slnx", SearchOption.TopDirectoryOnly))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (sln.Length <= 0)
            {
                throw new MmaCliException(
                    "invalid_solution_path",
                    $"The directory '{solutionPath}' does not contain a .sln or .slnx solution file.",
                    exitCode: 66);
            }

            var solutionName = Path.GetFileNameWithoutExtension(sln[0]);
            var projectsPath = ResolveProjectsPath(solutionPath, solutionName);

            return (solutionName, projectsPath);
        }

        public static string ResolveProjectsPath(string solutionPath, string solutionName)
        {
            // New 11-project .slnx flat layout: <solutionPath>/<SolutionName>.Core
            if (Directory.Exists(Path.Combine(solutionPath, $"{solutionName}.Core"))
                || Directory.Exists(Path.Combine(solutionPath, $"{solutionName}.AppApi")))
            {
                return solutionPath;
            }

            // Legacy nested layout: <solutionPath>/<SolutionName>/<SolutionName>.Core
            var nested = Path.Combine(solutionPath, solutionName);
            if (Directory.Exists(nested))
            {
                return nested;
            }

            return nested;
        }

        public static string ResolveModelFilePath(string projectsPath, string solutionName, string entityName, string modelSuffix)
        {
            var modelsRoot = Path.Combine(projectsPath, $"{solutionName}.Core", "Models");
            var candidates = new List<string>
            {
                Path.Combine(modelsRoot, entityName, $"{entityName}{modelSuffix}.cs"),
                Path.Combine(modelsRoot, entityName, $"{entityName}{modelSuffix}.g.cs"),
                Path.Combine(modelsRoot, $"{entityName}{modelSuffix}.cs"),
                Path.Combine(modelsRoot, $"{entityName}{modelSuffix}.g.cs"),
            };

            if (string.Equals(modelSuffix, "ModifyModel", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(modelsRoot, entityName, $"{entityName}Dto.cs"));
                candidates.Add(Path.Combine(modelsRoot, $"{entityName}Dto.cs"));
            }

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        public static string DetectModelParameterName(IEnumerable<string> lines, string signaturePrefix)
        {
            var signatureLine = lines.FirstOrDefault(l => l.Contains(signaturePrefix, StringComparison.Ordinal));
            if (signatureLine is null)
            {
                return "model";
            }

            var openParen = signatureLine.IndexOf('(');
            var closeParen = signatureLine.IndexOf(')', openParen + 1);
            if (openParen < 0 || closeParen <= openParen)
            {
                return "model";
            }

            var parameterList = signatureLine[(openParen + 1)..closeParen].Trim();
            var parts = parameterList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[^1].Trim() : "model";
        }
        public static void Help(string version)
        {

            Output.Warning($"""

.___  ___. .___  ___.      ___      
|   \/   | |   \/   |     /   \     
|  \  /  | |  \  /  |    /  ^  \    
|  |\/|  | |  |\/|  |   /  /_\  \   
|  |  |  | |  |  |  |  /  _____  \  
|__|  |__| |__|  |__| /__/     \__\ 


NAME:
   mma {version.Split('+')[0]}

USAGE:
   mma [global options] command [command options] [arguments...]

VERSION:
   {version.Split('+')[0]}

AUTHOR:
   Mohammed Abdelhay

COMMANDS:
   new, n       create a new solution in the current directory
   generate, g  create a new Component in the current solution
   ui           launch MMA CLI Studio visual schema designer (mma ui --help)
   help, h      Shows a list of commands or help for one command

COMMAND OPTIONS:
   new, n:
      SolutionName  Enter your solution name
      --mapper      Select the mapper package (allowed packages are Mapster | AutoMapper, default: AutoMapper)

   generate, g:
      entity, e     To indicate scaffolding of an entity, enter Entity name and Id type (default: Guid)
         property, p   To indicate scaffolding of a property, enter Entity name, Property name, Property datatype. To remove, add --remove flag
         relation, r   To indicate scaffolding of a relation, enter reference Entity name, child Entity name, ForeignKey datatype. To remove, add --remove flag
      --mapper      Select the mapper package (allowed packages are Mapster | AutoMapper, default: AutoMapper)

EXAMPLES:
   Create Solution:
      mma new MySolutionName --mapper Mapster
      mma n MySolutionName --mapper AutoMapper

   Generate Entity:
      mma generate entity MyEntity long --mapper Mapster
      mma g e MyEntity long --mapper Mapster

   Generate Property:
      mma generate property MyEntity MyProperty long
      mma g p MyEntity MyProperty long --remove

   Generate Relation:
      mma generate relation MyParentEntity MyChildEntity ForeignKeyProperty
      mma g r MyParentEntity MyChildEntity ForeignKeyProperty --remove

GLOBAL OPTIONS:
   --help, -h     Show help
   --version, -v  Print the version

""");
        }
    }
}

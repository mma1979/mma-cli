using Mma.Cli.Shared.Consts;

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

        public static string GetExecutablePath()
        {
            string executablePath = Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(executablePath);
        }

        public static string GetSetName(string componentName) =>
           componentName.EndsWith("s") ? $"{componentName}es" :
           componentName.EndsWith("y") ? $"{componentName.TrimEnd('y')}ies" :
           $"{componentName}s";

        public static (string solutionName, string projectsPath) CheckSolutionPath(string solutionPath)
        {
            var sln = Directory.GetFiles(solutionPath, "*.sln", SearchOption.TopDirectoryOnly);
            if (sln.Length <= 0)
            {
                Output.Error(
                    "ERROR: The current working directory does not contain a solution file");

                Environment.Exit(-1);

                return (string.Empty, string.Empty);
            }

            var slnName = new FileInfo(sln[0]).Name;
            var solutionName = slnName.AsSpan(0, slnName.Length - 4).ToString();
            var projectsPath = Path.Combine(solutionPath, solutionName);

            return (solutionName, projectsPath);
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
   mma {version}

USAGE:
   mma [global options] command [command options] [arguments...]

VERSION:
   {version}

AUTHOR:
   Mohammed Abdelhay

COMMANDS:
   new, n       create a new solution in the current directory
   generate, g  create a new Component in the current solution
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
            Environment.Exit(0);
        }
    }
}

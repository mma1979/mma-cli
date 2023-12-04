using CliWrap;
using Mma.Cli.Shared.Builders;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Helpers;
using Sharprompt;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Mma.Cli.AppV4
{
    public class Program
    {

        static async Task Main(string[] args)
        {
            // AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnCancelKeyPress;

            if (args.Length <= 0)
            {
                await InteractiveSession();
            }
            else
            {
                await CommandLineSession(args);
            }


        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {

            var dotnet = Process.GetProcessesByName("dotnet").ToList();
            var mma = Process.GetProcessesByName("mma-cli").ToList();
            if (dotnet.Any())
                dotnet.ForEach(p => p.Kill());

            if (mma.Any())
                mma.ForEach(p => p.Kill());

        }

       
        private static async Task CommandLineSession(string[] args)
        {
            switch (args[0])
            {
                case CommandsFlags.New:
                case CommandsFlags.NewShortHand:
                    SolutionBuilder.New(args)
                        .CreateSolutionDirectory()
                        .ExtractSolution()
                        .RootRenameAndReplace()
                        .RenameProjects()
                        .RenameCsprojFiles()
                        .ReplaceNamespaces()
                        .CreateMmaFolder();

                    Output.Success("Solution Created");
                    Environment.Exit(0);

                    break;

                case CommandsFlags.Generate:
                case CommandsFlags.GenerateShortHand:
                    HandleGenerate(args);
                    break;

                case CommandsFlags.UI:
                    await ExecuteUI();
                    break;

                case CommandsFlags.Import:
                    ImportFactory.New(args)
                        .Import();
                    break;

                case CommandsFlags.Help:
                case CommandsFlags.HelpShortHand:
                    BuildHelper.Help(version);
                    Environment.Exit(0);
                    break;

                case CommandsFlags.Version:
                case CommandsFlags.VersionShortHand:
                    Output.Success($"mma {version}");
                    Environment.Exit(0);
                    break;

                default:
                    Output.Error("Invalid Command");
                    BuildHelper.Help(version);
                    Environment.Exit(0);
                    break;
            }
        }


        private static void HandleGenerate(string[] args)
        {
            switch (args[1])
            {
                case ComponentFlags.Entity:
                case ComponentFlags.EntityShortHand:
                    EntityBuilder.New(args)
                    .GenerateDto()
                    .GenerateValidator()
                    .GenerateEntity()
                    .GenerateEntityConfig()
                    .DbContextMapping()
                    .GenerateService()
                    .GenerateController(!args.Contains(Flags.ApiFlag));
                    Output.Success("Entity files generated");
                    Environment.Exit(0);
                    break;

                case ComponentFlags.Property:
                case ComponentFlags.PropertyShortHand:
                    try
                    {
                        PropertiesBuilder.New(args, BuildHelper.DetectMapper())
                    .UpdateDto()
                    .UpdateEntity()
                    .UpdateEntityConfig();
                        Output.Success("Property has been generated");
                        Environment.Exit(0);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        Environment.Exit(-1);
                    }
                    break;

                case ComponentFlags.Relation:
                case ComponentFlags.RelationShortHand:
                    RelationsBuilder.New(args)
                        .UpdateParentDtos()
                        .UpdateChildDtos()
                        .UpdateParentEntity()
                        .UpdateChildEntity()
                        .UpdateParentEntityConfig();
                    Output.Success("Relation has been generated");
                    Environment.Exit(0);
                    break;

                default:
                    Output.Error("Invalid Component");
                    BuildHelper.Help(version);
                    Environment.Exit(0);
                    break;


            }
        }

        private static async Task InteractiveSession()
        {
            var command = Prompt.Select("Select your command", new[] { Commands.NEW, Commands.GENERATE, Commands.UI, Commands.WATCH }, defaultValue: Commands.NEW);
            var output = command switch
            {
                Commands.NEW => ExecuteNew(),
                Commands.GENERATE => ExecuteGenerate(),
                Commands.UI => await ExecuteUI(),
                Commands.WATCH => ExecutWatch(),
                _ => ExecuteInvalidCommand()
            };

            Environment.Exit(output ? 0 : -1);

        }

        private static bool ExecuteNew()
        {
            var solutionName = Prompt.Input<string>("Enter Solution Name");
            var mapper = Prompt.Select("Select the Mapper", new[] { Mappers.AutoMapper, Mappers.Mapster }, defaultValue: Mappers.AutoMapper);


            SolutionBuilder.New(solutionName, mapper)
                .CreateSolutionDirectory()
                .ExtractSolution()
                .RootRenameAndReplace()
                .RenameProjects()
                .RenameCsprojFiles()
                .ReplaceNamespaces()
                .CreateMmaFolder();

            Output.Success("Solution Created");
            return true;
        }

        private static bool ExecuteGenerate()
        {
            var componetType = Prompt.Select("Select Component", new[] { InteractiveComponents.AddEntity, InteractiveComponents.RemoveEntity, InteractiveComponents.AddProperty, InteractiveComponents.RemoveProperty, InteractiveComponents.AddRelation, InteractiveComponents.RemoveRelation }, defaultValue: InteractiveComponents.AddEntity);

            bool GenerateEntity(bool performRemove)
            {
                var entityName = Prompt.Input<string>("Enter Entity Name");
                var pkType = Prompt.Select("Select PK type", new[] { PkTypes.GUID, PkTypes.INT, PkTypes.LONG, PkTypes.DECIMAL, PkTypes.FLOOT, PkTypes.STRING, PkTypes.BOOL, PkTypes.DATE_TIME, }, defaultValue: PkTypes.GUID);
                var generateApi = Prompt.Select("Genereate API cotroller?", new[] { "Yes", "No" }, defaultValue: "Yes");

                var api = generateApi == "Yes" ? "" : "--no-api";
                var r = performRemove ? "--remove" : "";

                var mapper = BuildHelper.DetectMapper();
                EntityBuilder.New(new[] { "g", "e", entityName, pkType, Flags.MapperFlag, mapper, api, r })
                           .GenerateDto()
                           .GenerateValidator()
                           .GenerateEntity()
                           .GenerateEntityConfig()
                           .DbContextMapping()
                           .GenerateService()
                           .GenerateController(generateApi == "Yes");
                Output.Success("Entity files generated");
                Output.Warning($"Equivalent Commands is: \nmma g e {entityName} {pkType} --mapper {mapper} {api} {r}");
                return true;
            }

            bool GenerateProperty(bool performRemove)
            {
                var entityName = Prompt.Input<string>("Enter Entity Name");
                var propertyName = Prompt.Input<string>("Enter Property Name");
                var pType = Prompt.Select("Select Property type", new[] { PkTypes.GUID, PkTypes.INT, PkTypes.LONG, PkTypes.DECIMAL, PkTypes.FLOOT, PkTypes.STRING, PkTypes.BOOL, PkTypes.DATE_TIME, }, defaultValue: PkTypes.GUID);
                var nullable = Prompt.Select("Is Nullable?", new[] { "Yes", "No" }, defaultValue: "Yes") == "Yes";

                var n = nullable ? "true" : "false";
                var r = performRemove ? "--remove" : "";

                PropertiesBuilder.New(new[] { "g", "p", entityName, propertyName, pType, n, r }, BuildHelper.DetectMapper())
                    .UpdateDto()
                    .UpdateEntity()
                    .UpdateEntityConfig();
                Output.Success("Property has been generated");

                Output.Warning($"Equivalent Commands is: \nmma g p {entityName} {propertyName} {pType} {n} {r}");
                Environment.Exit(0);
                return true;
            }

            bool GenerateRelation(bool performRemove)
            {
                var parentEntityName = Prompt.Input<string>("Enter Reference Entity Name");
                var chiledEntityName = Prompt.Input<string>("Enter Child Entity Name");
                var foreignKeyName = Prompt.Input<string>("Enter ForiegnKey Name:");
                var fkType = Prompt.Select("Select Foreign Key data type", new[] { PkTypes.GUID, PkTypes.INT, PkTypes.LONG, PkTypes.DECIMAL, PkTypes.FLOOT, PkTypes.STRING, PkTypes.BOOL, PkTypes.DATE_TIME, }, defaultValue: PkTypes.GUID);

                var r = performRemove ? "--remove" : "";

                RelationsBuilder.New(new[] { "g", "r", parentEntityName, chiledEntityName, foreignKeyName, fkType, r })
                    .UpdateParentDtos()
                        .UpdateChildDtos()
                        .UpdateParentEntity()
                        .UpdateChildEntity()
                        .UpdateParentEntityConfig();
                Output.Success("Relation has been generated");
                Output.Warning($"Equivalent Commands is: \nmma g r {parentEntityName} {chiledEntityName} {foreignKeyName} {fkType} {r}");
                Environment.Exit(0);

                return true;
            }


            var result = componetType switch
            {
                InteractiveComponents.AddEntity => GenerateEntity(false),
                InteractiveComponents.RemoveEntity => GenerateEntity(true),
                InteractiveComponents.AddProperty => GenerateProperty(false),
                InteractiveComponents.RemoveProperty => GenerateProperty(true),
                InteractiveComponents.AddRelation => GenerateRelation(false),
                InteractiveComponents.RemoveRelation => GenerateRelation(true),
                _ => false
            };

            return result;
        }

        private static async Task<bool> ExecuteUI()
        {
            void OutputPipe(string o)
            {
                Console.Clear();
                Output.Success("Now listening on: http://localhost:5000");
            }

            void ErrorPipe(string o)
            {
                Output.Error(o);
            }

            string executablePath = BuildHelper.GetExecutablePath();
            var task = CliWrap.Cli.Wrap("dotnet")
                .WithWorkingDirectory($"{executablePath}\\UI")
                .WithArguments(a => a.Add("cli-ui.dll"))
                .WithStandardOutputPipe(PipeTarget.ToDelegate(OutputPipe))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(ErrorPipe))
                .ExecuteAsync();

            //processId = task.ProcessId;


            await task;

            return true;
        }

        private static bool ExecutWatch()
        {
            throw new NotImplementedException();
        }

        private static bool ExecuteInvalidCommand()
        {
            throw new NotImplementedException();
        }
              

        private static string version = Assembly.GetEntryAssembly()!
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.ToString();
    }
}

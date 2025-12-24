using CliWrap;

using Mma.Cli.Shared.Builders;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Helpers;

using Sharprompt;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public class Program
{
    private static readonly string Version = Assembly.GetEntryAssembly()!
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";

    static async Task Main(string[] args)
    {
        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            var exitCode = args.Length > 0
                ? await HandleCommandLineAsync(args)
                : await HandleInteractiveModeAsync();

            Environment.Exit(exitCode);
        }
        catch (Exception ex)
        {
            Output.Error($"An error occurred: {ex.Message}");
            Environment.Exit(-1);
        }
    }

    private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
    {
        KillProcessesByName("dotnet", "mma-cli");
    }

    private static void KillProcessesByName(params string[] processNames)
    {
        foreach (var processName in processNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    process.Kill();
                    process.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to kill {processName}: {ex.Message}");
            }
        }
    }

    private static async Task<int> HandleCommandLineAsync(string[] args)
    {
        var command = args[0].ToLowerInvariant();

        return command switch
        {
            CommandsFlags.New or CommandsFlags.NewShortHand => HandleNewCommand(args),
            CommandsFlags.Generate or CommandsFlags.GenerateShortHand => HandleGenerateCommand(args),
            CommandsFlags.UI => await HandleUICommand(),
            CommandsFlags.Import => HandleImportCommand(args),
            CommandsFlags.Help or CommandsFlags.HelpShortHand => HandleHelpCommand(),
            CommandsFlags.Version or CommandsFlags.VersionShortHand => HandleVersionCommand(),
            _ => HandleInvalidCommand()
        };
    }

    private static int HandleNewCommand(string[] args)
    {
        SolutionBuilder.New(args)
            .CreateSolutionDirectory()
            .ExtractSolution()
            .RootRenameAndReplace()
            .RenameProjects()
            .RenameCsprojFiles()
            .ReplaceNamespaces()
            .CreateMmaFolder();

        Output.Success("Solution Created");
        return 0;
    }

    private static int HandleGenerateCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Output.Error("Generate command requires a component type");
            return -1;
        }

        var component = args[1].ToLowerInvariant();

        return component switch
        {
            ComponentFlags.Entity or ComponentFlags.EntityShortHand => HandleEntityGeneration(args),
            ComponentFlags.Property or ComponentFlags.PropertyShortHand => HandlePropertyGeneration(args),
            ComponentFlags.Relation or ComponentFlags.RelationShortHand => HandleRelationGeneration(args),
            _ => HandleInvalidComponent()
        };
    }

    private static int HandleEntityGeneration(string[] args)
    {
        EntityBuilder.New(args)
            .GenerateModels()
            .GenerateValidator()
            .GenerateEntity()
            .GenerateEntityConfig()
            .DbContextMapping()
            .GenerateService()
            .GenerateController(!args.Contains(Flags.ApiFlag));

        Output.Success("Entity files generated");
        return 0;
    }

    private static int HandlePropertyGeneration(string[] args)
    {
        try
        {
            PropertiesBuilder.New(args, BuildHelper.DetectMapper())
                .UpdateEntityModels()
                .UpdateEntity()
                .UpdateEntityConfig();

            Output.Success("Property has been generated");
            return 0;
        }
        catch (Exception ex)
        {
            Output.Error($"Property generation failed: {ex.Message}");
            return -1;
        }
    }

    private static int HandleRelationGeneration(string[] args)
    {
        RelationsBuilder.New(args)
            .UpdateParentDtos()
            .UpdateChildDtos()
            .UpdateParentEntity()
            .UpdateChildEntity()
            .UpdateParentEntityConfig();

        Output.Success("Relation has been generated");
        return 0;
    }

    private static int HandleImportCommand(string[] args)
    {
        ImportFactory.New(args).Import();
        return 0;
    }

    private static async Task<int> HandleUICommand()
    {
        try
        {
            var executablePath = BuildHelper.GetExecutablePath();

            await CliWrap.Cli.Wrap("dotnet")
                .WithWorkingDirectory(Path.Combine(executablePath, "UI"))
                .WithArguments("cli-ui.dll")
                .WithStandardOutputPipe(PipeTarget.ToDelegate(_ => {
                    Console.Clear();
                    Output.Success("Now listening on: http://localhost:5000");
                }))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(Output.Error))
                .ExecuteAsync();

            return 0;
        }
        catch (Exception ex)
        {
            Output.Error($"UI execution failed: {ex.Message}");
            return -1;
        }
    }

    private static int HandleHelpCommand()
    {
        BuildHelper.Help(Version);
        return 0;
    }

    private static int HandleVersionCommand()
    {
        Output.Success($"""
.___  ___. .___  ___.      ___      
|   \/   | |   \/   |     /   \     
|  \  /  | |  \  /  |    /  ^  \    
|  |\/|  | |  |\/|  |   /  /_\  \   
|  |  |  | |  |  |  |  /  _____  \  
|__|  |__| |__|  |__| /__/     \__\ 

   mma {Version.Split('+')[0]}
""");
        return 0;
    }

    private static int HandleInvalidCommand()
    {
        Output.Error("Invalid Command");
        BuildHelper.Help(Version);
        return -1;
    }

    private static int HandleInvalidComponent()
    {
        Output.Error("Invalid Component");
        BuildHelper.Help(Version);
        return -1;
    }

    private static async Task<int> HandleInteractiveModeAsync()
    {
        var command = Prompt.Select("Select your command",
            new[] { Commands.NEW, Commands.GENERATE, Commands.UI, Commands.WATCH },
            defaultValue: Commands.NEW);

        return command switch
        {
            Commands.NEW => HandleInteractiveNew(),
            Commands.GENERATE => HandleInteractiveGenerate(),
            Commands.UI => await HandleUICommand(),
            Commands.WATCH => HandleWatch(),
            _ => HandleInvalidCommand()
        };
    }

    private static int HandleInteractiveNew()
    {
        var solutionName = Prompt.Input<string>("Enter Solution Name");
        var mapper = Prompt.Select("Select the Mapper",
            new[] { Mappers.AutoMapper, Mappers.Mapster },
            defaultValue: Mappers.AutoMapper);

        SolutionBuilder.New(solutionName, mapper)
            .CreateSolutionDirectory()
            .ExtractSolution()
            .RootRenameAndReplace()
            .RenameProjects()
            .RenameCsprojFiles()
            .ReplaceNamespaces()
            .CreateMmaFolder();

        Output.Success("Solution Created");
        return 0;
    }

    private static int HandleInteractiveGenerate()
    {
        var componentType = Prompt.Select("Select Component",
            new[] {
                InteractiveComponents.AddEntity, InteractiveComponents.RemoveEntity,
                InteractiveComponents.AddProperty, InteractiveComponents.RemoveProperty,
                InteractiveComponents.AddRelation, InteractiveComponents.RemoveRelation
            },
            defaultValue: InteractiveComponents.AddEntity);

        return componentType switch
        {
            InteractiveComponents.AddEntity => GenerateEntityInteractive(false),
            InteractiveComponents.RemoveEntity => GenerateEntityInteractive(true),
            InteractiveComponents.AddProperty => GeneratePropertyInteractive(false),
            InteractiveComponents.RemoveProperty => GeneratePropertyInteractive(true),
            InteractiveComponents.AddRelation => GenerateRelationInteractive(false),
            InteractiveComponents.RemoveRelation => GenerateRelationInteractive(true),
            _ => -1
        };
    }

    private static int GenerateEntityInteractive(bool performRemove)
    {
        var entityName = Prompt.Input<string>("Enter Entity Name");
        var pkType = Prompt.Select("Select PK type",
            new[] { PkTypes.GUID, PkTypes.INT, PkTypes.LONG, PkTypes.DECIMAL, PkTypes.FLOAT, PkTypes.STRING, PkTypes.BOOL, PkTypes.DATE_TIME },
            defaultValue: PkTypes.GUID);
        var generateApi = Prompt.Select("Generate API controller?", new[] { "Yes", "No" }, defaultValue: "Yes") == "Yes";

        var args = BuildEntityArgs(entityName, pkType, generateApi, performRemove);

        EntityBuilder.New(args)
            .GenerateModels()
            .GenerateValidator()
            .GenerateEntity()
            .GenerateEntityConfig()
            .DbContextMapping()
            .GenerateService()
            .GenerateController(generateApi);

        Output.Success("Entity files generated");
        LogEquivalentCommand("entity", entityName, pkType, generateApi, performRemove);
        return 0;
    }

    private static int GeneratePropertyInteractive(bool performRemove)
    {
        var entityName = Prompt.Input<string>("Enter Entity Name");
        var propertyName = Prompt.Input<string>("Enter Property Name");
        var pType = Prompt.Select("Select Property type",
            new[] { PkTypes.GUID, PkTypes.INT, PkTypes.LONG, PkTypes.DECIMAL, PkTypes.FLOAT, PkTypes.STRING, PkTypes.BOOL, PkTypes.DATE_TIME },
            defaultValue: PkTypes.GUID);
        var nullable = Prompt.Select("Is Nullable?", new[] { "Yes", "No" }, defaultValue: "Yes") == "Yes";

        var args = BuildPropertyArgs(entityName, propertyName, pType, nullable, performRemove);

        PropertiesBuilder.New(args, BuildHelper.DetectMapper())
            .UpdateEntityModels()
            .UpdateEntity()
            .UpdateEntityConfig();

        Output.Success("Property has been generated");
        LogEquivalentCommand("property", entityName, propertyName, pType, nullable, performRemove);
        return 0;
    }

    private static int GenerateRelationInteractive(bool performRemove)
    {
        var parentEntityName = Prompt.Input<string>("Enter Reference Entity Name");
        var childEntityName = Prompt.Input<string>("Enter Child Entity Name");
        var foreignKeyName = Prompt.Input<string>("Enter Foreign Key Name:");
        var fkType = Prompt.Select("Select Foreign Key data type",
            new[] { PkTypes.GUID, PkTypes.INT, PkTypes.LONG, PkTypes.DECIMAL, PkTypes.FLOAT, PkTypes.STRING, PkTypes.BOOL, PkTypes.DATE_TIME },
            defaultValue: PkTypes.GUID);

        var args = BuildRelationArgs(parentEntityName, childEntityName, foreignKeyName, fkType, performRemove);

        RelationsBuilder.New(args)
            .UpdateParentDtos()
            .UpdateChildDtos()
            .UpdateParentEntity()
            .UpdateChildEntity()
            .UpdateParentEntityConfig();

        Output.Success("Relation has been generated");
        LogEquivalentCommand("relation", parentEntityName, childEntityName, foreignKeyName, fkType, performRemove);
        return 0;
    }

    private static int HandleWatch()
    {
        Output.Error("Watch command is not yet implemented");
        return -1;
    }

    // Helper methods for building command arguments
    private static string[] BuildEntityArgs(string entityName, string pkType, bool generateApi, bool performRemove)
    {
        var args = new List<string> { "g", "e", entityName, pkType, Flags.MapperFlag, BuildHelper.DetectMapper() };
        if (!generateApi) args.Add("--no-api");
        if (performRemove) args.Add("--remove");
        return args.ToArray();
    }

    private static string[] BuildPropertyArgs(string entityName, string propertyName, string pType, bool nullable, bool performRemove)
    {
        var args = new List<string> { "g", "p", entityName, propertyName, pType, nullable.ToString().ToLower() };
        if (performRemove) args.Add("--remove");
        return args.ToArray();
    }

    private static string[] BuildRelationArgs(string parentEntity, string childEntity, string foreignKey, string fkType, bool performRemove)
    {
        var args = new List<string> { "g", "r", parentEntity, childEntity, foreignKey, fkType };
        if (performRemove) args.Add("--remove");
        return args.ToArray();
    }

    private static void LogEquivalentCommand(string type, params object[] parameters)
    {
        var commandParts = new List<string> { "mma", "g" };
        commandParts.AddRange(parameters.Select(p => p.ToString()));
        Output.Warning($"Equivalent Command: {string.Join(" ", commandParts)}");
    }
}
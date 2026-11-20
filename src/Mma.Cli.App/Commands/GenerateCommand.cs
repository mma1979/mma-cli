using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.App.Output;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Results;
using Spectre.Console;

namespace Mma.Cli.App.Commands;

public static class GenerateCommand
{
    public static Command Create(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        Option<bool> jsonOption)
    {
        var generateCommand = new Command("generate", "Scaffold or update entities, properties, and relations.");
        generateCommand.Aliases.Add("g");

        generateCommand.Add(CreateEntityCommand(services, console, outputWriter, currentDirectoryProvider, jsonOption));
        generateCommand.Add(CreatePropertyCommand(services, console, outputWriter, currentDirectoryProvider, jsonOption));
        generateCommand.Add(CreateRelationCommand(services, console, outputWriter, currentDirectoryProvider, jsonOption));

        return generateCommand;
    }

    private static Command CreateEntityCommand(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        Option<bool> jsonOption)
    {
        var nameArg = new Argument<string>("name") { Description = "Entity class name." };
        var pkTypeArg = new Argument<string>("pkType")
        {
            Description = "Primary key data type (default: Guid).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "Guid"
        };

        var mapperOption = new Option<string?>("--mapper", "-m")
        {
            Description = "Override mapper (AutoMapper or Mapster)."
        };
        var noApiOption = new Option<bool>("--no-api")
        {
            Description = "Skip generating the REST API controller."
        };
        var solutionOption = new Option<string?>("--solution", "-s")
        {
            Description = "Path to the target solution directory."
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Preview generated files without writing to disk."
        };

        var command = new Command("entity", "Generate a new entity across Core, EntityFramework, Services, and AppApi.");
        command.Aliases.Add("e");
        command.Add(nameArg);
        command.Add(pkTypeArg);
        command.Add(mapperOption);
        command.Add(noApiOption);
        command.Add(solutionOption);
        command.Add(dryRunOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var name = parseResult.GetRequiredValue(nameArg);
            var pkType = parseResult.GetRequiredValue(pkTypeArg);
            var mapperRaw = parseResult.GetValue(mapperOption);
            var noApi = parseResult.GetValue(noApiOption);
            var solutionRaw = parseResult.GetValue(solutionOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var json = parseResult.GetValue(jsonOption);

            return await ExecuteEntityAsync(
                services,
                console,
                outputWriter,
                currentDirectoryProvider,
                name,
                pkType,
                mapperRaw,
                noApi,
                solutionRaw,
                dryRun,
                json,
                ct).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreatePropertyCommand(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        Option<bool> jsonOption)
    {
        var entityArg = new Argument<string>("entity") { Description = "Target entity name." };
        var propertyArg = new Argument<string>("property") { Description = "Property name." };
        var dataTypeArg = new Argument<string>("dataType")
        {
            Description = "Property data type (default: Guid).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "Guid"
        };
        var nullableArg = new Argument<bool>("nullable")
        {
            Description = "Whether the property is nullable (default: false).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false
        };

        var removeOption = new Option<bool>("--remove")
        {
            Description = "Remove the property instead of adding it."
        };
        var solutionOption = new Option<string?>("--solution", "-s")
        {
            Description = "Path to the target solution directory."
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Preview changes without modifying disk."
        };

        var command = new Command("property", "Add or remove a property on an entity and its models/configuration.");
        command.Aliases.Add("p");
        command.Add(entityArg);
        command.Add(propertyArg);
        command.Add(dataTypeArg);
        command.Add(nullableArg);
        command.Add(removeOption);
        command.Add(solutionOption);
        command.Add(dryRunOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var entity = parseResult.GetRequiredValue(entityArg);
            var property = parseResult.GetRequiredValue(propertyArg);
            var dataType = parseResult.GetRequiredValue(dataTypeArg);
            var nullable = parseResult.GetRequiredValue(nullableArg);
            var remove = parseResult.GetValue(removeOption);
            var solutionRaw = parseResult.GetValue(solutionOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var json = parseResult.GetValue(jsonOption);

            return await ExecutePropertyAsync(
                services,
                console,
                outputWriter,
                currentDirectoryProvider,
                entity,
                property,
                dataType,
                nullable,
                remove,
                solutionRaw,
                dryRun,
                json,
                ct).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateRelationCommand(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        Option<bool> jsonOption)
    {
        var parentArg = new Argument<string>("parent") { Description = "Parent (one) entity name." };
        var childArg = new Argument<string>("child") { Description = "Child (many) entity name." };
        var foreignKeyArg = new Argument<string>("foreignKey") { Description = "Foreign key property name." };
        var fkTypeArg = new Argument<string>("fkType")
        {
            Description = "Foreign key data type (default: Guid).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "Guid"
        };

        var removeOption = new Option<bool>("--remove")
        {
            Description = "Remove the relation instead of adding it."
        };
        var solutionOption = new Option<string?>("--solution", "-s")
        {
            Description = "Path to the target solution directory."
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Preview changes without modifying disk."
        };

        var command = new Command("relation", "Add or remove a 1:N relation between parent and child entities.");
        command.Aliases.Add("r");
        command.Add(parentArg);
        command.Add(childArg);
        command.Add(foreignKeyArg);
        command.Add(fkTypeArg);
        command.Add(removeOption);
        command.Add(solutionOption);
        command.Add(dryRunOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var parent = parseResult.GetRequiredValue(parentArg);
            var child = parseResult.GetRequiredValue(childArg);
            var foreignKey = parseResult.GetRequiredValue(foreignKeyArg);
            var fkType = parseResult.GetRequiredValue(fkTypeArg);
            var remove = parseResult.GetValue(removeOption);
            var solutionRaw = parseResult.GetValue(solutionOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var json = parseResult.GetValue(jsonOption);

            return await ExecuteRelationAsync(
                services,
                console,
                outputWriter,
                currentDirectoryProvider,
                parent,
                child,
                foreignKey,
                fkType,
                remove,
                solutionRaw,
                dryRun,
                json,
                ct).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task<int> ExecuteEntityAsync(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        string name,
        string pkType,
        string? mapperRaw,
        bool noApi,
        string? solutionRaw,
        bool dryRun,
        bool json,
        CancellationToken ct)
    {
        MapperKind? mapperOverride = null;
        if (!string.IsNullOrWhiteSpace(mapperRaw))
        {
            if (!MapperKindParser.TryParse(mapperRaw, out var parsedMapper))
            {
                CliOutputFormatter.WriteFailure(
                    console,
                    outputWriter,
                    json,
                    "generate entity",
                    ErrorCodes.VALIDATION_ERROR,
                    $"Invalid mapper '{mapperRaw}'. Supported values: AutoMapper, Mapster.");
                return 1;
            }

            mapperOverride = parsedMapper;
        }

        var solutionPath = CliApp.ResolvePath(solutionRaw, currentDirectoryProvider);

        await using var scope = services.CreateAsyncScope();
        var entityService = scope.ServiceProvider.GetRequiredService<IEntityService>();
        var progress = CliOutputFormatter.CreateProgress(console, json);

        var result = await entityService.GenerateAsync(
            new AddEntityRequest(solutionPath, name, pkType, mapperOverride, GenerateApi: !noApi, DryRun: dryRun),
            progress,
            ct).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "generate entity",
                result.Error,
                $"Failed to generate entity '{name}'.");
            return 1;
        }

        var warnings = new List<string>(result.Warnings);
        if (!dryRun)
        {
            await CliApp.SyncWorkspaceFromManifestAsync(services, solutionPath, warnings, ct).ConfigureAwait(false);
        }

        var data = new
        {
            entityName = name,
            pkType,
            generateApi = !noApi,
            solutionPath,
            dryRun,
            result.Value.CreatedFiles,
            result.Value.ModifiedFiles,
            result.Value.DeletedFiles
        };

        CliOutputFormatter.WriteSuccess(
            console,
            outputWriter,
            json,
            "generate entity",
            data,
            dryRun
                ? $"Dry run: entity '{name}' planned for '{solutionPath}'."
                : $"Entity '{name}' generated in '{solutionPath}'.",
            result.Value,
            warnings);

        return 0;
    }

    internal static async Task<int> ExecutePropertyAsync(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        string entity,
        string property,
        string dataType,
        bool nullable,
        bool remove,
        string? solutionRaw,
        bool dryRun,
        bool json,
        CancellationToken ct)
    {
        var solutionPath = CliApp.ResolvePath(solutionRaw, currentDirectoryProvider);

        await using var scope = services.CreateAsyncScope();
        var propertyService = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var progress = CliOutputFormatter.CreateProgress(console, json);

        var result = await propertyService.ApplyAsync(
            new AddPropertyRequest(solutionPath, entity, property, dataType, nullable, remove, dryRun),
            progress,
            ct).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "generate property",
                result.Error,
                $"Failed to update property '{entity}.{property}'.");
            return 1;
        }

        var warnings = new List<string>(result.Warnings);
        if (!dryRun)
        {
            await CliApp.SyncWorkspaceFromManifestAsync(services, solutionPath, warnings, ct).ConfigureAwait(false);
        }

        var data = new
        {
            entityName = entity,
            propertyName = property,
            dataType,
            nullable,
            remove,
            solutionPath,
            dryRun,
            result.Value.CreatedFiles,
            result.Value.ModifiedFiles,
            result.Value.DeletedFiles
        };

        var verb = remove ? "removed" : "added";
        CliOutputFormatter.WriteSuccess(
            console,
            outputWriter,
            json,
            "generate property",
            data,
            dryRun
                ? $"Dry run: property '{entity}.{property}' {(remove ? "removal" : "addition")} planned."
                : $"Property '{entity}.{property}' {verb}.",
            result.Value,
            warnings);

        return 0;
    }

    internal static async Task<int> ExecuteRelationAsync(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        string parent,
        string child,
        string foreignKey,
        string fkType,
        bool remove,
        string? solutionRaw,
        bool dryRun,
        bool json,
        CancellationToken ct)
    {
        var solutionPath = CliApp.ResolvePath(solutionRaw, currentDirectoryProvider);

        await using var scope = services.CreateAsyncScope();
        var relationService = scope.ServiceProvider.GetRequiredService<IRelationService>();
        var progress = CliOutputFormatter.CreateProgress(console, json);

        var result = await relationService.ApplyAsync(
            new AddRelationRequest(solutionPath, parent, child, foreignKey, fkType, remove, dryRun),
            progress,
            ct).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "generate relation",
                result.Error,
                $"Failed to update relation '{parent} -> {child}'.");
            return 1;
        }

        var warnings = new List<string>(result.Warnings);
        if (!dryRun)
        {
            await CliApp.SyncWorkspaceFromManifestAsync(services, solutionPath, warnings, ct).ConfigureAwait(false);
        }

        var data = new
        {
            parentEntity = parent,
            childEntity = child,
            foreignKey,
            foreignKeyType = fkType,
            remove,
            solutionPath,
            dryRun,
            result.Value.CreatedFiles,
            result.Value.ModifiedFiles,
            result.Value.DeletedFiles
        };

        var verb = remove ? "removed" : "added";
        CliOutputFormatter.WriteSuccess(
            console,
            outputWriter,
            json,
            "generate relation",
            data,
            dryRun
                ? $"Dry run: relation '{parent} -> {child}' {(remove ? "removal" : "addition")} planned."
                : $"Relation '{parent} -> {child}' ({foreignKey}) {verb}.",
            result.Value,
            warnings);

        return 0;
    }
}

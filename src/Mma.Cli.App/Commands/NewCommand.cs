using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.App.Output;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Results;
using Mma.Cli.Database;
using Spectre.Console;

namespace Mma.Cli.App.Commands;

public static class NewCommand
{
    public static Command Create(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        Option<bool> jsonOption)
    {
        var solutionNameArg = new Argument<string>("solutionName")
        {
            Description = "Name of the solution to create (dot-separated C# identifiers)."
        };

        var mapperOption = new Option<string>("--mapper", "-m")
        {
            Description = "Mapper package to use (AutoMapper or Mapster).",
            DefaultValueFactory = _ => "AutoMapper"
        };

        var outputOption = new Option<string?>("--output", "-o")
        {
            Description = "Parent directory in which to create the solution directory."
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Preview solution creation without writing files or updating the workspace registry."
        };

        var command = new Command("new", "Create a new Clean Architecture solution.");
        command.Aliases.Add("n");
        command.Add(solutionNameArg);
        command.Add(mapperOption);
        command.Add(outputOption);
        command.Add(dryRunOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var solutionName = parseResult.GetRequiredValue(solutionNameArg);
            var mapperRaw = parseResult.GetRequiredValue(mapperOption);
            var outputRaw = parseResult.GetValue(outputOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var json = parseResult.GetValue(jsonOption);

            return await ExecuteAsync(
                services,
                console,
                outputWriter,
                currentDirectoryProvider,
                solutionName,
                mapperRaw,
                outputRaw,
                dryRun,
                json,
                ct).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        string solutionName,
        string mapperRaw,
        string? outputRaw,
        bool dryRun,
        bool json,
        CancellationToken ct)
    {
        if (!MapperKindParser.TryParse(mapperRaw, out var mapperKind))
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "new",
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid mapper '{mapperRaw}'. Supported values: AutoMapper, Mapster.");
            return 1;
        }

        var parentDir = CliApp.ResolvePath(outputRaw, currentDirectoryProvider);

        await using var scope = services.CreateAsyncScope();
        var solutionService = scope.ServiceProvider.GetRequiredService<ISolutionService>();
        var progress = CliOutputFormatter.CreateProgress(console, json);

        var result = await solutionService.CreateAsync(
            new CreateSolutionRequest(parentDir, solutionName, mapperKind, dryRun),
            progress,
            ct).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "new",
                result.Error,
                $"Failed to create solution '{solutionName}'.");
            return 1;
        }

        var solutionPath = Path.Combine(parentDir, solutionName);
        int? workspaceId = null;

        if (!dryRun)
        {
            var registry = scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
            await registry.EnsureInitializedAsync(ct).ConfigureAwait(false);
            var record = await registry.RegisterAsync(
                solutionName,
                solutionPath,
                mapperKind.ToFolderName(),
                ct).ConfigureAwait(false);
            workspaceId = record.Id;
        }

        var data = new
        {
            solutionName,
            solutionPath,
            mapper = mapperKind.ToFolderName(),
            dryRun,
            workspaceId,
            result.Value.CreatedFiles,
            result.Value.ModifiedFiles,
            result.Value.DeletedFiles
        };

        CliOutputFormatter.WriteSuccess(
            console,
            outputWriter,
            json,
            "new",
            data,
            dryRun
                ? $"Dry run: solution '{solutionName}' planned at '{solutionPath}'."
                : $"Solution '{solutionName}' created at '{solutionPath}'.",
            result.Value,
            result.Warnings);

        return 0;
    }
}

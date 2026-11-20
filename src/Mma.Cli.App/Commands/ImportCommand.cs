using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.App.Output;
using Mma.Cli.Core.Export;
using Spectre.Console;

namespace Mma.Cli.App.Commands;

public static class ImportCommand
{
    public static Command Create(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        Option<bool> jsonOption)
    {
        var solutionPathArg = new Argument<string?>("solutionPath")
        {
            Description = "Path to the target solution directory (defaults to current directory).",
            Arity = ArgumentArity.ZeroOrOne
        };

        var command = new Command("import", "Export .mma/project.mma to the configured generator API.");
        command.Add(solutionPathArg);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var solutionPathRaw = parseResult.GetValue(solutionPathArg);
            var json = parseResult.GetValue(jsonOption);

            return await ExecuteAsync(
                services,
                console,
                outputWriter,
                currentDirectoryProvider,
                solutionPathRaw,
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
        string? solutionPathRaw,
        bool json,
        CancellationToken ct)
    {
        var solutionPath = CliApp.ResolvePath(solutionPathRaw, currentDirectoryProvider);

        await using var scope = services.CreateAsyncScope();
        var exporter = scope.ServiceProvider.GetRequiredService<ISolutionExporter>();
        var result = await exporter.ExportAsync(solutionPath, ct).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "import",
                result.Error,
                $"Failed to import solution '{solutionPath}'.");
            return 1;
        }

        CliOutputFormatter.WriteSuccess(
            console,
            outputWriter,
            json,
            "import",
            new { solutionPath, exported = true },
            $"Solution manifest imported successfully from '{solutionPath}'.",
            warnings: result.Warnings);

        return 0;
    }
}

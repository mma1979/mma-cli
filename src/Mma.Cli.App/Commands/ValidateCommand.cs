using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.App.Output;
using Mma.Cli.Core.Manifest;
using Spectre.Console;

namespace Mma.Cli.App.Commands;

public static class ValidateCommand
{
    public static Command Create(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        Option<bool> jsonOption)
    {
        var solutionOption = new Option<string?>("--solution", "-s")
        {
            Description = "Path to the target solution directory."
        };

        var command = new Command("validate", "Validate .mma/project.mma manifest against files on disk.");
        command.Add(solutionOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var solutionRaw = parseResult.GetValue(solutionOption);
            var json = parseResult.GetValue(jsonOption);

            return await ExecuteAsync(
                services,
                console,
                outputWriter,
                currentDirectoryProvider,
                solutionRaw,
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
        string? solutionRaw,
        bool json,
        CancellationToken ct)
    {
        var solutionPath = CliApp.ResolvePath(solutionRaw, currentDirectoryProvider);

        await using var scope = services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IProjectValidator>();
        var result = await validator.ValidateAsync(solutionPath, ct).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "validate",
                result.Error,
                $"Validation failed for '{solutionPath}'.");
            return 1;
        }

        CliOutputFormatter.WriteValidationReport(console, outputWriter, json, result.Value);
        return result.Value.IsValid ? 0 : 1;
    }
}

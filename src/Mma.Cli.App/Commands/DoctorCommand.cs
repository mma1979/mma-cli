using System.CommandLine;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.App.Output;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Process;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Database;
using Spectre.Console;

namespace Mma.Cli.App.Commands;

public static class DoctorCommand
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
            Description = "Optional path to a solution directory to include in diagnostics."
        };

        var command = new Command("doctor", "Verify .NET SDK, dotnet-ef, workspace database, and active solution health.");
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
        await using var scope = services.CreateAsyncScope();
        var dotnetRunner = scope.ServiceProvider.GetRequiredService<IDotnetRunner>();
        var locator = scope.ServiceProvider.GetRequiredService<ISolutionLocator>();
        var validator = scope.ServiceProvider.GetRequiredService<IProjectValidator>();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();

        var cwd = currentDirectoryProvider();
        var processWorkingDir = Directory.Exists(cwd) ? cwd : AppContext.BaseDirectory;

        var sdkResult = await dotnetRunner.RunAsync(processWorkingDir, ["--version"], ct: ct).ConfigureAwait(false);
        var sdkVersion = sdkResult.IsSuccess ? FirstNonEmptyLine(sdkResult.Value?.StandardOutput) : null;
        var sdkCheck = sdkResult.IsSuccess && !string.IsNullOrWhiteSpace(sdkVersion)
            ? new DoctorDotnetCheck(true, sdkVersion, "Ok", $".NET SDK {sdkVersion} available.")
            : new DoctorDotnetCheck(false, null, "Error", sdkResult.Error?.Message ?? "dotnet SDK not found.");

        var efResult = await dotnetRunner.RunAsync(processWorkingDir, ["ef", "--version"], ct: ct).ConfigureAwait(false);
        var efVersion = efResult.IsSuccess ? FirstNonEmptyLine(efResult.Value?.StandardOutput) : null;
        var efCheck = efResult.IsSuccess && !string.IsNullOrWhiteSpace(efVersion)
            ? new DoctorDotnetCheck(true, efVersion, "Ok", $"dotnet-ef {efVersion} available.")
            : new DoctorDotnetCheck(false, null, "Warning", "dotnet-ef tool not installed (optional).");

        var dbPath = ResolveEffectiveDbPath(dbContext);
        bool dbAccessible;
        string dbStatus;
        try
        {
            await registry.EnsureInitializedAsync(ct).ConfigureAwait(false);
            dbAccessible = await dbContext.Database.CanConnectAsync(ct).ConfigureAwait(false);
            dbStatus = dbAccessible ? "Ok" : "Error";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            dbAccessible = false;
            dbStatus = "Error";
            dbPath = $"{dbPath} ({ex.Message})";
        }

        var (solutionCheck, criticalSolutionFailure) = await EvaluateSolutionAsync(
            locator,
            validator,
            currentDirectoryProvider,
            solutionRaw,
            ct).ConfigureAwait(false);

        var report = new DoctorReport(
            sdkCheck,
            efCheck,
            dbPath,
            dbAccessible,
            dbStatus,
            solutionCheck);

        var passed = sdkCheck.Available && dbAccessible && !criticalSolutionFailure;
        CliOutputFormatter.WriteDoctorReport(console, outputWriter, json, report, passed);
        return passed ? 0 : 1;
    }

    private static async Task<(DoctorSolutionCheck Check, bool CriticalFailure)> EvaluateSolutionAsync(
        ISolutionLocator locator,
        IProjectValidator validator,
        Func<string> currentDirectoryProvider,
        string? solutionRaw,
        CancellationToken ct)
    {
        var explicitSolution = !string.IsNullOrWhiteSpace(solutionRaw);
        var targetDir = CliApp.ResolvePath(solutionRaw, currentDirectoryProvider);

        var located = locator.Locate(targetDir);
        if (!located.IsSuccess || located.Value is null)
        {
            if (explicitSolution)
            {
                return (
                    new DoctorSolutionCheck(
                        false,
                        null,
                        targetDir,
                        false,
                        0,
                        "Error",
                        located.Error?.Message ?? $"Solution not found at '{targetDir}'."),
                    true);
            }

            return (
                new DoctorSolutionCheck(
                    false,
                    null,
                    null,
                    null,
                    0,
                    "Info",
                    "Not in a solution directory"),
                false);
        }

        var info = located.Value;
        var manifestPath = Path.Combine(info.SolutionPath, ".mma", "project.mma");
        if (!explicitSolution && !File.Exists(manifestPath))
        {
            return (
                new DoctorSolutionCheck(
                    true,
                    info.SolutionName,
                    info.SolutionPath,
                    null,
                    0,
                    "Info",
                    "Solution found without .mma/project.mma manifest"),
                false);
        }

        var validation = await validator.ValidateAsync(info.SolutionPath, ct).ConfigureAwait(false);
        if (!validation.IsSuccess || validation.Value is null)
        {
            return (
                new DoctorSolutionCheck(
                    true,
                    info.SolutionName,
                    info.SolutionPath,
                    false,
                    0,
                    explicitSolution ? "Error" : "Warning",
                    validation.Error?.Message ?? "Unable to validate project manifest."),
                explicitSolution);
        }

        var valReport = validation.Value;
        var status = valReport.IsValid
            ? (valReport.Issues.Count > 0 ? "Warning" : "Ok")
            : (explicitSolution ? "Error" : "Warning");

        var msg = valReport.IsValid
            ? (valReport.Issues.Count > 0
                ? $"Valid with {valReport.Issues.Count} warning(s)"
                : "Valid")
            : $"Validation failed with {valReport.Issues.Count} issue(s)";

        return (
            new DoctorSolutionCheck(
                true,
                valReport.SolutionName,
                info.SolutionPath,
                valReport.IsValid,
                valReport.Issues.Count,
                status,
                msg),
            explicitSolution && !valReport.IsValid);
    }

    private static string ResolveEffectiveDbPath(WorkspaceDbContext dbContext)
    {
        var defaultResolved = WorkspaceDbPathResolver.Resolve();
        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return defaultResolved;
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.DataSource)
                ? defaultResolved
                : builder.DataSource;
        }
        catch (ArgumentException)
        {
            return defaultResolved;
        }
    }

    private static string? FirstNonEmptyLine(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = rawLine.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                return trimmed;
            }
        }

        return null;
    }
}

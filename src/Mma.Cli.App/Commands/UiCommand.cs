using System.CommandLine;
using CliWrap;
using Mma.Cli.App.Output;
using Mma.Cli.Core.Results;
using Spectre.Console;

namespace Mma.Cli.App.Commands;

public static class UiCommand
{
    public static System.CommandLine.Command Create(
        IAnsiConsole console,
        TextWriter outputWriter,
        Option<bool> jsonOption)
    {
        var portOption = new Option<int>("--port", "-p")
        {
            Description = "TCP port for the local UI server (default: 5180).",
            DefaultValueFactory = _ => 5180
        };

        var command = new System.CommandLine.Command("ui", "Launch the local MMA web UI.");
        command.Add(portOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var port = parseResult.GetRequiredValue(portOption);
            var json = parseResult.GetValue(jsonOption);

            return await ExecuteAsync(console, outputWriter, port, json, ct).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(
        IAnsiConsole console,
        TextWriter outputWriter,
        int port,
        bool json,
        CancellationToken ct)
    {
        if (port is < 1 or > 65535)
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "ui",
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid port '{port}'. Port must be between 1 and 65535.");
            return 1;
        }

        var baseDir = AppContext.BaseDirectory;
        var bffDll = Path.Combine(baseDir, "bff", "Mma.Cli.Bff.dll");
        var legacyUiDll = Path.Combine(baseDir, "UI", "cli-ui.dll");

        string? targetDll = File.Exists(bffDll)
            ? bffDll
            : File.Exists(legacyUiDll)
                ? legacyUiDll
                : null;

        if (targetDll is null)
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "ui",
                ErrorCodes.IO_ERROR,
                $"UI server assembly not found. Checked '{bffDll}' and '{legacyUiDll}'.");
            return 1;
        }

        var url = $"http://127.0.0.1:{port}";
        var workingDir = Path.GetDirectoryName(targetDll) ?? baseDir;

        if (!json)
        {
            console.MarkupLine($"[green]Starting UI at[/] [link]{Markup.Escape(url)}[/] ({Markup.Escape(targetDll)})...");
            console.MarkupLine("[grey]Press Ctrl+C to stop the UI server.[/]");
        }

        try
        {
            var stdoutPipe = json
                ? PipeTarget.Null
                : PipeTarget.ToDelegate(line =>
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        console.MarkupLine($"[grey]{Markup.Escape(line)}[/]");
                    }
                });
            var stderrPipe = json
                ? PipeTarget.Null
                : PipeTarget.ToDelegate(line =>
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        console.MarkupLine($"[red]{Markup.Escape(line)}[/]");
                    }
                });

            var result = await CliWrap.Cli.Wrap("dotnet")
                .WithWorkingDirectory(workingDir)
                .WithArguments([targetDll, "--urls", url])
                .WithEnvironmentVariables(env => env.Set("ASPNETCORE_URLS", url))
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(stdoutPipe)
                .WithStandardErrorPipe(stderrPipe)
                .ExecuteAsync(ct)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                CliOutputFormatter.WriteFailure(
                    console,
                    outputWriter,
                    json,
                    "ui",
                    ErrorCodes.PROCESS_FAILED,
                    $"UI host exited with code {result.ExitCode}.");
                return 1;
            }

            CliOutputFormatter.WriteSuccess(
                console,
                outputWriter,
                json,
                "ui",
                new { url, assembly = targetDll, exitCode = result.ExitCode },
                "UI host stopped.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            CliOutputFormatter.WriteSuccess(
                console,
                outputWriter,
                json,
                "ui",
                new { url, assembly = targetDll, cancelled = true },
                "UI host stopped.");
            return 0;
        }
        catch (Exception ex)
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "ui",
                ErrorCodes.PROCESS_FAILED,
                $"UI execution failed: {ex.Message}",
                ex.ToString());
            return 1;
        }
    }
}

using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
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
            Description = "TCP port for the local UI server (default: an ephemeral loopback port).",
            DefaultValueFactory = _ => 0
        };
        var headlessCheckOption = new Option<bool>("--headless-check")
        {
            Description = "Verify that the packaged BFF payload is available without launching a browser."
        };

        var command = new System.CommandLine.Command("ui", "Launch the local MMA web UI.");
        command.Add(portOption);
        command.Add(headlessCheckOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var port = parseResult.GetRequiredValue(portOption);
            var json = parseResult.GetValue(jsonOption);
            var headlessCheck = parseResult.GetValue(headlessCheckOption);

            return await ExecuteAsync(console, outputWriter, port, json, headlessCheck, ct).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(
        IAnsiConsole console,
        TextWriter outputWriter,
        int port,
        bool json,
        bool headlessCheck,
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
        var targetDll = Path.Combine(baseDir, "bff", "Mma.Cli.Bff.dll");
        if (!File.Exists(targetDll))
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "ui",
                ErrorCodes.IO_ERROR,
                $"Packaged BFF assembly was not found at '{targetDll}'.");
            return 1;
        }

        if (headlessCheck)
        {
            CliOutputFormatter.WriteSuccess(console, outputWriter, json, "ui", new { assembly = targetDll }, "Packaged UI payload is available.");
            return 0;
        }

        if (port == 0)
        {
            port = GetAvailableLoopbackPort();
        }

        var url = $"http://127.0.0.1:{port}";
        var bootstrapToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
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

            var processTask = CliWrap.Cli.Wrap("dotnet")
                .WithWorkingDirectory(workingDir)
                .WithArguments([targetDll, "--urls", url])
                .WithEnvironmentVariables(env =>
                {
                    env.Set("ASPNETCORE_URLS", url);
                    env.Set("MMA_BFF_BOOTSTRAP_TOKEN", bootstrapToken);
                })
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(stdoutPipe)
                .WithStandardErrorPipe(stderrPipe)
                .ExecuteAsync(ct);

            TryOpenBrowser($"{url}/?token={Uri.EscapeDataString(bootstrapToken)}");
            var result = await processTask.ConfigureAwait(false);

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

    private static int GetAvailableLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // The BFF remains available even when a headless environment cannot open a browser.
        }
    }
}

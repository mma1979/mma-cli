using System.CommandLine;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.App.Output;
using Mma.Cli.Core.Export;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;
using Spectre.Console;

namespace Mma.Cli.App.Commands;

public static class WatchCommand
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(1);

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

        var command = new Command("watch", "Watch .mma/project.mma for changes and re-import automatically.");
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
        var candidatePath = CliApp.ResolvePath(solutionPathRaw, currentDirectoryProvider);

        await using var scope = services.CreateAsyncScope();
        var locator = scope.ServiceProvider.GetRequiredService<ISolutionLocator>();
        var exporter = scope.ServiceProvider.GetRequiredService<ISolutionExporter>();

        var located = locator.Locate(candidatePath);
        if (!located.IsSuccess || located.Value is null)
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "watch",
                located.Error,
                $"Could not locate solution at '{candidatePath}'.");
            return 1;
        }

        var info = located.Value;
        var mmaDir = Path.Combine(info.SolutionPath, ".mma");
        if (!Directory.Exists(mmaDir))
        {
            CliOutputFormatter.WriteFailure(
                console,
                outputWriter,
                json,
                "watch",
                ErrorCodes.IO_ERROR,
                $"Directory '.mma' was not found in '{info.SolutionPath}'.");
            return 1;
        }

        if (!json)
        {
            console.MarkupLine($"[green]Watching[/] [bold]{Markup.Escape(info.SolutionName)}[/] ({Markup.Escape(mmaDir)})...");
            console.MarkupLine("[grey]Press Ctrl+C to stop watching.[/]");
        }

        var signalChannel = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

        using var watcher = new FileSystemWatcher(mmaDir, "project.mma")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false
        };

        void OnWatcherEvent(object sender, FileSystemEventArgs e) => signalChannel.Writer.TryWrite(1);
        void OnWatcherRenamed(object sender, RenamedEventArgs e)
        {
            if (string.Equals(e.Name, "project.mma", StringComparison.OrdinalIgnoreCase))
            {
                signalChannel.Writer.TryWrite(1);
            }
        }

        watcher.Changed += OnWatcherEvent;
        watcher.Created += OnWatcherEvent;
        watcher.Renamed += OnWatcherRenamed;
        watcher.EnableRaisingEvents = true;

        var exportCount = 0;
        var lastExportUtc = DateTimeOffset.MinValue;

        try
        {
            while (await signalChannel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (signalChannel.Reader.TryRead(out _))
                {
                }

                // Coalesce burst filesystem events and enforce a 1-second debounce window.
                var elapsed = DateTimeOffset.UtcNow - lastExportUtc;
                if (elapsed < DebounceInterval)
                {
                    await Task.Delay(DebounceInterval - elapsed, ct).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
                }

                while (signalChannel.Reader.TryRead(out _))
                {
                }

                lastExportUtc = DateTimeOffset.UtcNow;

                if (!json)
                {
                    console.MarkupLine("[yellow]Change detected in project.mma; re-importing...[/]");
                }

                var exportResult = await exporter.ExportAsync(info.SolutionPath, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                if (exportResult.IsSuccess)
                {
                    exportCount++;
                    if (!json)
                    {
                        console.MarkupLine("[green]Re-import succeeded.[/]");
                    }
                }
                else if (!json)
                {
                    console.MarkupLine(
                        $"[red]Re-import failed:[/] {Markup.Escape(exportResult.Error?.Message ?? "Unknown error")}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean cooperative shutdown via CancellationToken.
        }
        finally
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnWatcherEvent;
            watcher.Created -= OnWatcherEvent;
            watcher.Renamed -= OnWatcherRenamed;
        }

        CliOutputFormatter.WriteSuccess(
            console,
            outputWriter,
            json,
            "watch",
            new { solutionPath = info.SolutionPath, solutionName = info.SolutionName, exportCount, stopped = true },
            $"Stopped watching '{info.SolutionName}'.");

        return 0;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using CliWrap;

using Mma.Cli.Shared.Exceptions;
using Mma.Cli.Shared.Helpers;

namespace Mma.Cli.AppV4;

internal static class UiLauncher
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = UiLaunchOptions.Parse(args);
        if (options.ShowHelp)
        {
            Output.Warning(UiLaunchOptions.HelpText);
            return 0;
        }

        var uiDir = Path.Combine(BuildHelper.GetExecutablePath(), "UI");
        var bffDll = Path.Combine(uiDir, "Mma.Cli.Bff.dll");
        if (!File.Exists(bffDll))
        {
            throw new MmaCliException(
                "ui_bundle_missing",
                $"UI bundle not found at '{bffDll}'. Reinstall mma-cli or run scripts/build-ui.ps1.",
                exitCode: 78);
        }

        var port = options.Port == 0 ? GetAvailableLoopbackPort() : options.Port;
        var baseUrl = $"http://127.0.0.1:{port}";
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var initialPath = ResolveInitialPath(options.InitialPath);

        var env = new Dictionary<string, string?>
        {
            ["ASPNETCORE_URLS"] = baseUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["MMA_CLI_MODE"] = "local",
            ["MMA_UI_TOKEN"] = token,
            ["MMA_INITIAL_PATH"] = initialPath,
            ["Introspection__NoLocalDb"] = options.AllowLocalDb ? "false" : "true",
        };

        Output.Warning($"Starting MMA CLI Studio on {baseUrl} ...");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var serverTask = CliWrap.Cli.Wrap("dotnet")
            .WithWorkingDirectory(uiDir)
            .WithArguments("Mma.Cli.Bff.dll")
            .WithEnvironmentVariables(env)
            .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Output.Error(line);
                }
            }))
            .ExecuteAsync(linkedCts.Token);

        var ready = await WaitForHealthAsync(baseUrl, linkedCts.Token);
        if (!ready)
        {
            linkedCts.Cancel();
            throw new MmaCliException(
                "ui_startup_timeout",
                $"MMA CLI Studio did not become ready at {baseUrl}/api/health within 15 seconds.",
                exitCode: 1);
        }

        var launchUrl = $"{baseUrl}/?token={token}";
        Output.Success($"MMA CLI Studio is running at {baseUrl}");
        Output.Warning("Press Ctrl+C to stop.");

        if (options.OpenBrowser)
        {
            TryOpenBrowser(launchUrl);
        }

        try
        {
            await serverTask;
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Output.Warning("Shutting down MMA CLI Studio...");
            return 0;
        }
    }

    private static string? ResolveInitialPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var cwd = Directory.GetCurrentDirectory();
        var hasSln = Directory.EnumerateFiles(cwd, "*.sln", SearchOption.TopDirectoryOnly).Any()
            || Directory.EnumerateFiles(cwd, "*.slnx", SearchOption.TopDirectoryOnly).Any();
        return hasSln ? cwd : null;
    }

    private static int GetAvailableLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<bool> WaitForHealthAsync(string baseUrl, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await http.GetAsync($"{baseUrl}/api/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // Keep polling until server binds or deadline expires.
            }

            await Task.Delay(200, cancellationToken);
        }

        return false;
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            Output.Warning($"Open your browser to: {url}");
        }
    }
}

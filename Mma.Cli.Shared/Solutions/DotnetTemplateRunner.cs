using System.ComponentModel;
using System.Text;

using CliWrap;

using Mma.Cli.Shared.Exceptions;

namespace Mma.Cli.Shared.Solutions;

public sealed class DotnetTemplateRunner : IDotnetTemplateRunner
{
    private readonly string? _customHivePath;

    public DotnetTemplateRunner(string? customHivePath = null)
    {
        _customHivePath = string.IsNullOrWhiteSpace(customHivePath) ? null : customHivePath.Trim();
    }

    public async Task<bool> IsTemplateInstalledAsync(
        string shortName,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "new", "list", shortName };
        AppendCustomHive(args);

        var result = await ExecuteDotnetAsync(args, cancellationToken);
        if (!result.Succeeded)
        {
            return false;
        }

        return result.StdOut.Contains(shortName, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string?> GetInstalledPackageVersionAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "new", "uninstall" };
        AppendCustomHive(args);

        var result = await ExecuteDotnetAsync(args, cancellationToken);
        if (!result.Succeeded)
        {
            return null;
        }

        return ParseInstalledPackageVersion(result.StdOut, packageId);
    }

    public static string? ParseInstalledPackageVersion(string stdout, string packageId)
    {
        if (string.IsNullOrWhiteSpace(stdout) || string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        var lines = stdout.Split(["\r\n", "\n"], StringSplitOptions.None);
        var inMatchingPackage = false;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            var normalizedHeader = trimmed.TrimStart('>').Trim();

            if (string.Equals(normalizedHeader, packageId, StringComparison.OrdinalIgnoreCase))
            {
                inMatchingPackage = true;
                continue;
            }

            if (!inMatchingPackage)
            {
                continue;
            }

            if (trimmed.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
            {
                var version = trimmed["Version:".Length..].Trim();
                return string.IsNullOrWhiteSpace(version) ? null : version;
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal)
                && !normalizedHeader.StartsWith("dotnet new uninstall", StringComparison.OrdinalIgnoreCase))
            {
                inMatchingPackage = false;
            }
        }

        return null;
    }

    public async Task<TemplateCommandResult> InstallPackageAsync(
        string packageId,
        string? version = null,
        string? nugetSource = null,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var args = BuildInstallArguments(packageId, version, nugetSource, force, _customHivePath);
        return await ExecuteDotnetAsync(args, cancellationToken);
    }

    public async Task<TemplateCommandResult> InstantiateAsync(
        string shortName,
        string solutionName,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "new",
            shortName,
            "-n",
            solutionName,
            "-o",
            outputDirectory
        };
        AppendCustomHive(args);

        return await ExecuteDotnetAsync(args, cancellationToken);
    }

    public static IReadOnlyList<string> BuildInstallArguments(
        string packageId,
        string? version,
        string? nugetSource,
        bool force,
        string? customHivePath = null)
    {
        var args = new List<string> { "new", "install" };
        var resolvedLocalNupkg = TryResolveLocalNupkg(packageId, version, nugetSource);

        if (resolvedLocalNupkg is not null)
        {
            args.Add(resolvedLocalNupkg);
        }
        else
        {
            var packageSpec = string.IsNullOrWhiteSpace(version)
                ? packageId
                : $"{packageId}::{version.Trim()}";
            args.Add(packageSpec);

            if (!string.IsNullOrWhiteSpace(nugetSource))
            {
                args.Add("--nuget-source");
                args.Add(nugetSource.Trim());
            }
        }

        if (force)
        {
            args.Add("--force");
        }

        if (!string.IsNullOrWhiteSpace(customHivePath))
        {
            args.Add("--debug:custom-hive");
            args.Add(customHivePath.Trim());
        }

        return args;
    }

    private static string? TryResolveLocalNupkg(string packageId, string? version, string? nugetSource)
    {
        if (string.IsNullOrWhiteSpace(nugetSource))
        {
            return null;
        }

        var trimmed = nugetSource.Trim();
        if (File.Exists(trimmed) && trimmed.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(trimmed);
        }

        if (!Directory.Exists(trimmed))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            var exact = Path.Combine(trimmed, $"{packageId}.{version.Trim()}.nupkg");
            if (File.Exists(exact))
            {
                return Path.GetFullPath(exact);
            }
        }

        var matching = Directory
            .GetFiles(trimmed, $"{packageId}.*.nupkg", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return matching is null ? null : Path.GetFullPath(matching);
    }

    private void AppendCustomHive(List<string> args)
    {
        if (!string.IsNullOrWhiteSpace(_customHivePath))
        {
            args.Add("--debug:custom-hive");
            args.Add(_customHivePath);
        }
    }

    private static async Task<TemplateCommandResult> ExecuteDotnetAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try
        {
            var result = await CliWrap.Cli.Wrap("dotnet")
                .WithArguments(arguments)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdout))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr))
                .ExecuteAsync(cancellationToken);

            return new TemplateCommandResult(
                Succeeded: result.ExitCode == 0,
                StdOut: stdout.ToString().Trim(),
                StdErr: stderr.ToString().Trim(),
                ExitCode: result.ExitCode);
        }
        catch (Win32Exception ex)
        {
            throw new MmaCliException(
                "dotnet_sdk_missing",
                "The 'dotnet' SDK CLI was not found on PATH. Install the .NET SDK to scaffold solutions from NuGet templates.",
                exitCode: 127,
                innerException: ex);
        }
    }
}

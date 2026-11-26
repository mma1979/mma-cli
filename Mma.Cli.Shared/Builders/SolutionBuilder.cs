using System.Text;

using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Exceptions;
using Mma.Cli.Shared.Helpers;
using Mma.Cli.Shared.Solutions;

using Newtonsoft.Json;

namespace Mma.Cli.Shared.Builders
{
    public class SolutionBuilder
    {
        public string SolutionName { get; private set; } = "";
        public string SolutionPath { get; private set; } = "";
        public string ProjectsPath { get; private set; } = "";
        public string Mapper { get; private set; } = Mappers.AutoMapper;
        public string? TemplateSource { get; private set; }
        public string? TemplateVersion { get; private set; }

        public SolutionBuilder(string solutionName)
        {
            SolutionName = solutionName;
        }

        public static SolutionBuilder New(
            string solutionName,
            string mapper,
            string? templateSource = null,
            string? templateVersion = null)
        {
            return new SolutionBuilder(solutionName)
            {
                Mapper = string.Equals(mapper?.Trim(), Mappers.Mapster, StringComparison.OrdinalIgnoreCase)
                    ? Mappers.Mapster
                    : Mappers.AutoMapper,
                TemplateSource = templateSource,
                TemplateVersion = templateVersion
            };
        }

        public static SolutionBuilder New(string[] args)
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                throw new MmaCliException(
                    "invalid_solution_name",
                    "Solution name is required. Usage: mma new <SolutionName> [--mapper AutoMapper|Mapster] [--template-source <path-or-url>] [--template-version <version>]",
                    exitCode: 2);
            }

            var solutionName = args[1].Trim();
            var mapperValue = GetFlagValue(args, Flags.MapperFlag);
            var mapper = string.Equals(mapperValue, Mappers.Mapster, StringComparison.OrdinalIgnoreCase)
                ? Mappers.Mapster
                : Mappers.AutoMapper;

            var templateSource = GetFlagValue(args, Flags.TemplateSourceFlag);
            var templateVersion = GetFlagValue(args, Flags.TemplateVersionFlag);

            return new SolutionBuilder(solutionName)
            {
                Mapper = mapper,
                TemplateSource = templateSource,
                TemplateVersion = templateVersion
            };
        }

        public SolutionBuilder CreateSolutionDirectory() =>
            CreateSolutionDirectory(Directory.GetCurrentDirectory());

        public SolutionBuilder CreateSolutionDirectory(string parentDirectory)
        {
            var path = Path.Combine(parentDirectory, SolutionName);
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            {
                throw new MmaCliException(
                    "directory_not_empty",
                    $"Target directory '{path}' already exists and is not empty.",
                    exitCode: 1);
            }

            Output.Warning($"Creating {path}");
            _ = Directory.CreateDirectory(path);
            SolutionPath = path;
            ProjectsPath = Path.Combine(path, SolutionName);
            return this;
        }

        public async Task<SolutionBuilder> ScaffoldFromTemplateAsync(
            IDotnetTemplateRunner templateRunner,
            string? templateSource = null,
            string? templateVersion = null,
            bool forceInstall = false,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(templateRunner);

            if (string.IsNullOrWhiteSpace(SolutionPath))
            {
                CreateSolutionDirectory();
            }

            var descriptor = SolutionTemplateCatalog.Resolve(Mapper);
            var resolvedSource = SolutionTemplateCatalog.ResolveTemplateSource(templateSource ?? TemplateSource);
            var explicitVersion = SolutionTemplateCatalog.ResolveTemplateVersion(templateVersion ?? TemplateVersion);

            var isInstalled = await templateRunner.IsTemplateInstalledAsync(descriptor.ShortName, cancellationToken);
            var shouldInstall = !isInstalled || forceInstall || !string.IsNullOrWhiteSpace(explicitVersion);

            if (shouldInstall)
            {
                var targetVersion = explicitVersion ?? descriptor.DefaultVersion;
                Output.Warning($"Installing solution template '{descriptor.PackageId}' ({targetVersion})...");

                var installResult = await templateRunner.InstallPackageAsync(
                    descriptor.PackageId,
                    targetVersion,
                    resolvedSource,
                    force: forceInstall || !string.IsNullOrWhiteSpace(explicitVersion),
                    cancellationToken);

                if (!installResult.Succeeded)
                {
                    var detail = string.IsNullOrWhiteSpace(installResult.StdErr)
                        ? installResult.StdOut
                        : installResult.StdErr;
                    throw new MmaCliException(
                        "template_install_failed",
                        $"Failed to install template package '{descriptor.PackageId}' ({targetVersion}). " +
                        $"If offline or using a private feed, pass '{Flags.TemplateSourceFlag} <path-or-url>' or set '{SolutionTemplateCatalog.TemplateSourceEnvVar}'. " +
                        $"Details: {detail}",
                        exitCode: installResult.ExitCode == 0 ? 1 : installResult.ExitCode);
                }
            }

            var scaffoldResult = await templateRunner.InstantiateAsync(
                descriptor.ShortName,
                SolutionName,
                SolutionPath,
                cancellationToken);

            if (!scaffoldResult.Succeeded)
            {
                var detail = string.IsNullOrWhiteSpace(scaffoldResult.StdErr)
                    ? scaffoldResult.StdOut
                    : scaffoldResult.StdErr;
                throw new MmaCliException(
                    "template_scaffold_failed",
                    $"Failed to scaffold solution '{SolutionName}' from template '{descriptor.ShortName}'. Details: {detail}",
                    exitCode: scaffoldResult.ExitCode == 0 ? 1 : scaffoldResult.ExitCode);
            }

            ProjectsPath = BuildHelper.ResolveProjectsPath(SolutionPath, SolutionName);
            return this;
        }

        public SolutionBuilder CreateMmaFolder()
        {
            var mmaDir = Directory.CreateDirectory(Path.Combine(SolutionPath, ".mma"));
            mmaDir.Attributes = FileAttributes.Directory | FileAttributes.Hidden;

            var p = new
            {
                Project = new
                {
                    Name = SolutionName,
                    Path = SolutionPath
                },
                Entities = new List<object>(),
                Rows = new List<object>(),
                Relations = new List<object>()
            };

            using StreamWriter writer = new(Path.Combine(mmaDir.FullName, "project.mma"), false, Encoding.UTF8);
            writer.Write(JsonConvert.SerializeObject(p));
            writer.Flush();
            writer.Close();

            return this;
        }

        private static string? GetFlagValue(string[] args, string flagName)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flagName, StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = args[i + 1];
                    return string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith("--", StringComparison.Ordinal)
                        ? null
                        : candidate.Trim();
                }
            }

            return null;
        }
    }
}

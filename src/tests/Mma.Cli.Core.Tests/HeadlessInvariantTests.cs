using System.Reflection;
using System.Text;
using Mma.Cli.Core.Services;
using Mma.Cli.Database;

namespace Mma.Cli.Core.Tests;

public sealed class HeadlessInvariantTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Mma.Cli.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Mma.Cli.sln.");
    }

    [Fact]
    public void CoreSourceFiles_ContainZeroForbiddenHeadlessApis()
    {
        var coreDir = Path.Combine(FindRepoRoot(), "src", "Mma.Cli.Core");
        var csFiles = Directory.GetFiles(coreDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(csFiles);

        var forbidden = new[]
        {
            "Environment.Exit",
            "Console.",
            "HttpContext",
            "Newtonsoft.Json",
            "RestSharp",
            "Mma.Cli.Shared",
            "Mma.Cli.Database",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.Data.Sqlite",
            "Sharprompt"
        };

        foreach (var file in csFiles)
        {
            var codeOnly = StripCommentsAndStrings(File.ReadAllText(file));
            foreach (var token in forbidden)
            {
                Assert.DoesNotContain(token, codeOnly, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void CoreAssembly_ReferencesZeroForbiddenAssemblies()
    {
        Assembly coreAssembly = typeof(SolutionService).Assembly;
        var referencedNames = coreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var forbiddenAssemblies = new[]
        {
            "Newtonsoft.Json",
            "RestSharp",
            "Mma.Cli.Shared",
            "Mma.Cli.Database",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Sqlite",
            "Microsoft.Data.Sqlite",
            "Sharprompt"
        };

        foreach (var forbidden in forbiddenAssemblies)
        {
            Assert.DoesNotContain(forbidden, referencedNames);
        }
    }

    [Fact]
    public void DatabaseSourceFiles_ContainZeroForbiddenHeadlessApis()
    {
        var dbDir = Path.Combine(FindRepoRoot(), "src", "Mma.Cli.Database");
        var csFiles = Directory.GetFiles(dbDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(csFiles);

        var forbidden = new[]
        {
            "Environment.Exit",
            "Console.",
            "HttpContext",
            "Newtonsoft.Json",
            "RestSharp",
            "Mma.Cli.Shared",
            "Sharprompt"
        };

        foreach (var file in csFiles)
        {
            var raw = File.ReadAllText(file);
            Assert.DoesNotContain("wwwroot", raw, StringComparison.OrdinalIgnoreCase);

            var codeOnly = StripCommentsAndStrings(raw);
            foreach (var token in forbidden)
            {
                Assert.DoesNotContain(token, codeOnly, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void DatabaseAssembly_ReferencesZeroForbiddenAssemblies()
    {
        Assembly dbAssembly = typeof(WorkspaceDbContext).Assembly;
        var referencedNames = dbAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var forbiddenAssemblies = new[]
        {
            "Newtonsoft.Json",
            "RestSharp",
            "Mma.Cli.Shared",
            "Sharprompt"
        };

        foreach (var forbidden in forbiddenAssemblies)
        {
            Assert.DoesNotContain(forbidden, referencedNames);
        }
    }

    private static string StripCommentsAndStrings(string source)
    {
        var sb = new StringBuilder(source.Length);
        var i = 0;

        while (i < source.Length)
        {
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }
                sb.Append('\n');
                continue;
            }

            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    i++;
                }
                i += 2;
                sb.Append(' ');
                continue;
            }

            if (i + 2 < source.Length && source[i] == '"' && source[i + 1] == '"' && source[i + 2] == '"')
            {
                var quoteCount = 0;
                while (i < source.Length && source[i] == '"')
                {
                    quoteCount++;
                    i++;
                }

                while (i < source.Length)
                {
                    if (source[i] == '"')
                    {
                        var closing = 0;
                        while (i < source.Length && source[i] == '"')
                        {
                            closing++;
                            i++;
                        }
                        if (closing >= quoteCount)
                        {
                            break;
                        }
                    }
                    else
                    {
                        i++;
                    }
                }
                sb.Append("\"\"");
                continue;
            }

            if (i + 1 < source.Length && (source[i] == '@' && source[i + 1] == '"'
                                          || (i + 2 < source.Length && ((source[i] == '$' && source[i + 1] == '@') || (source[i] == '@' && source[i + 1] == '$')) && source[i + 2] == '"')))
            {
                while (i < source.Length && source[i] != '"')
                {
                    i++;
                }
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '"')
                    {
                        if (i + 1 < source.Length && source[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    i++;
                }
                sb.Append("\"\"");
                continue;
            }

            if (source[i] == '"' || (i + 1 < source.Length && source[i] == '$' && source[i + 1] == '"'))
            {
                if (source[i] == '$')
                {
                    i++;
                }
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }
                    if (source[i] == '"')
                    {
                        i++;
                        break;
                    }
                    i++;
                }
                sb.Append("\"\"");
                continue;
            }

            sb.Append(source[i]);
            i++;
        }

        return sb.ToString();
    }
}

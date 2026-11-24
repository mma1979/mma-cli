using Mma.Cli.Shared.Exceptions;
using Mma.Cli.Shared.Helpers;

using Xunit;

namespace Mma.Cli.Shared.Tests;

public sealed class UiLaunchOptionsTests
{
    [Fact]
    public void Parse_DefaultArgs_UsesPort5080_OpensBrowser_AllowsLocalDb()
    {
        var opts = UiLaunchOptions.Parse(["ui"]);

        Assert.Equal(5080, opts.Port);
        Assert.Null(opts.InitialPath);
        Assert.True(opts.OpenBrowser);
        Assert.True(opts.AllowLocalDb);
        Assert.False(opts.ShowHelp);
    }

    [Fact]
    public void Parse_CustomOptions_ParsesAllFlags()
    {
        var opts = UiLaunchOptions.Parse(["ui", "--port", "0", "--no-browser", "--no-local-db", "--path", "."]);

        Assert.Equal(0, opts.Port);
        Assert.False(opts.OpenBrowser);
        Assert.False(opts.AllowLocalDb);
        Assert.Equal(Path.GetFullPath("."), opts.InitialPath);
    }

    [Fact]
    public void Parse_InvalidPort_ThrowsMmaCliExceptionWithExitCode2()
    {
        var ex = Assert.Throws<MmaCliException>(() => UiLaunchOptions.Parse(["ui", "--port", "99999"]));

        Assert.Equal("invalid_port", ex.Code);
        Assert.Equal(2, ex.ExitCode);
    }

    [Fact]
    public void Parse_HelpFlag_SetsShowHelp()
    {
        var opts = UiLaunchOptions.Parse(["ui", "--help"]);
        Assert.True(opts.ShowHelp);
    }
}

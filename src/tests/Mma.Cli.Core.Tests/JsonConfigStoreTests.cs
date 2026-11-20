using Mma.Cli.Core.Config;

namespace Mma.Cli.Core.Tests;

public sealed class JsonConfigStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _solutionDir;
    private readonly string _globalConfigDir;

    public JsonConfigStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-config-{Guid.NewGuid():N}");
        _solutionDir = Path.Combine(_tempRoot, "solution");
        _globalConfigDir = Path.Combine(_tempRoot, "global-mma");
        Directory.CreateDirectory(Path.Combine(_solutionDir, ".mma"));
        Directory.CreateDirectory(_globalConfigDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void GetSetting_LocalConfigOverridesGlobalConfig()
    {
        File.WriteAllText(
            Path.Combine(_globalConfigDir, "config.json"),
            """{"GeneratorEndpoint": "https://global.example.com"}""");
        File.WriteAllText(
            Path.Combine(_solutionDir, ".mma", "config.json"),
            """{"GeneratorEndpoint": "https://local.example.com"}""");

        var store = new JsonConfigStore(userConfigDir: _globalConfigDir);
        var value = store.GetSetting(_solutionDir, "GeneratorEndpoint", "https://default.example.com");

        Assert.Equal("https://local.example.com", value);
    }

    [Fact]
    public void GetSetting_WhenLocalKeyIsNull_FallsThroughToGlobalConfig()
    {
        File.WriteAllText(
            Path.Combine(_globalConfigDir, "config.json"),
            """{"GeneratorEndpoint": "https://global.example.com"}""");
        File.WriteAllText(
            Path.Combine(_solutionDir, ".mma", "config.json"),
            """{"GeneratorEndpoint": null}""");

        var store = new JsonConfigStore(userConfigDir: _globalConfigDir);
        var value = store.GetSetting(_solutionDir, "GeneratorEndpoint", "https://default.example.com");

        Assert.Equal("https://global.example.com", value);
    }

    [Fact]
    public void GetSetting_WhenBothLocalAndGlobalKeysAreNull_ReturnsDefaultValue()
    {
        File.WriteAllText(
            Path.Combine(_globalConfigDir, "config.json"),
            """{"GeneratorEndpoint": null}""");
        File.WriteAllText(
            Path.Combine(_solutionDir, ".mma", "config.json"),
            """{"GeneratorEndpoint": null}""");

        var store = new JsonConfigStore(userConfigDir: _globalConfigDir);
        var value = store.GetSetting(_solutionDir, "GeneratorEndpoint", "https://default.example.com");

        Assert.Equal("https://default.example.com", value);
    }

    [Fact]
    public void GetSetting_WhenLocalJsonIsMalformed_FallsThroughToGlobalWithoutThrowing()
    {
        File.WriteAllText(
            Path.Combine(_globalConfigDir, "config.json"),
            """{"GeneratorEndpoint": "https://global.example.com"}""");
        File.WriteAllText(
            Path.Combine(_solutionDir, ".mma", "config.json"),
            "{ invalid json !!!");

        var store = new JsonConfigStore(userConfigDir: _globalConfigDir);
        var value = store.GetSetting(_solutionDir, "GeneratorEndpoint", "https://default.example.com");

        Assert.Equal("https://global.example.com", value);
    }

    [Fact]
    public void GetSetting_WhenGlobalJsonIsMalformed_ReturnsDefaultValueWithoutThrowing()
    {
        File.WriteAllText(
            Path.Combine(_globalConfigDir, "config.json"),
            "{ broken");

        var store = new JsonConfigStore(userConfigDir: _globalConfigDir);
        var value = store.GetSetting(_solutionDir, "GeneratorEndpoint", "https://default.example.com");

        Assert.Equal("https://default.example.com", value);
    }

    [Fact]
    public void GetSetting_SupportsJsonCommentsTrailingCommasAndPrimitives()
    {
        File.WriteAllText(
            Path.Combine(_solutionDir, ".mma", "config.json"),
            """
            {
              // comment
              "AllowInsecureLocalhostTls": true,
              "RetryCount": 42,
            }
            """);

        var store = new JsonConfigStore(userConfigDir: _globalConfigDir);

        Assert.Equal("true", store.GetSetting(_solutionDir, "AllowInsecureLocalhostTls", "false"), ignoreCase: true);
        Assert.Equal("42", store.GetSetting(_solutionDir, "RetryCount", "0"));
    }
}

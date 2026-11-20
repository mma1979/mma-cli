using System.Net;
using System.Text;
using Mma.Cli.Core.Config;
using Mma.Cli.Core.Export;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;

namespace Mma.Cli.Core.Tests;

public sealed class SolutionExporterTests : IDisposable
{
    private readonly string _tempRoot;

    public SolutionExporterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("https://localhost:44357", false, false)]
    [InlineData("https://example.com", true, false)]
    [InlineData("http://localhost:5000", true, false)]
    [InlineData("https://localhost:44357", true, true)]
    [InlineData("https://127.0.0.1:44357", true, true)]
    [InlineData("https://[::1]:44357", true, true)]
    public void CreateHttpMessageHandler_GatesInsecureTlsAndDisablesAutoRedirectStrictlyToHttpsLoopback(
        string baseUrl,
        bool allowInsecureLocalhostTls,
        bool expectsInsecure)
    {
        using var handler = Assert.IsType<HttpClientHandler>(
            SolutionExporter.CreateHttpMessageHandler(baseUrl, allowInsecureLocalhostTls));

        if (expectsInsecure)
        {
            Assert.Same(
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                handler.ServerCertificateCustomValidationCallback);
            Assert.False(handler.AllowAutoRedirect);
        }
        else
        {
            Assert.Null(handler.ServerCertificateCustomValidationCallback);
            Assert.True(handler.AllowAutoRedirect);
        }
    }

    [Fact]
    public async Task ExportAsync_PostsProjectManifestJsonToImportEndpoint()
    {
        var slnDir = CreateSampleSolutionDir("ExportSample", """{"GeneratorEndpoint":"https://export.local:5443"}""");
        var recordingHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var factory = new FakeHttpClientFactory(recordingHandler);
        var exporter = new SolutionExporter(
            new SolutionLocator(),
            new JsonConfigStore(userConfigDir: Path.Combine(_tempRoot, "empty-global")),
            factory);

        var result = await exporter.ExportAsync(slnDir);

        Assert.True(result.IsSuccess);
        Assert.Equal(SolutionExporter.HttpClientName, factory.LastClientName);
        Assert.Equal(HttpMethod.Post, recordingHandler.LastMethod);
        Assert.Equal(new Uri("https://export.local:5443/api/projects/import"), recordingHandler.LastRequestUri);
        Assert.Contains("ExportSample", recordingHandler.LastBody);
    }

    [Fact]
    public async Task ExportAsync_WithAllowInsecureLocalhostTlsOnLoopback_SelectsInsecureLocalhostClient()
    {
        var slnDir = CreateSampleSolutionDir(
            "ExportLoopback",
            """{"GeneratorEndpoint":"https://localhost:44357","AllowInsecureLocalhostTls":true}""");
        var recordingHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var factory = new FakeHttpClientFactory(recordingHandler);
        var exporter = new SolutionExporter(
            new SolutionLocator(),
            new JsonConfigStore(userConfigDir: Path.Combine(_tempRoot, "empty-global")),
            factory);

        var result = await exporter.ExportAsync(slnDir);

        Assert.True(result.IsSuccess);
        Assert.Equal(SolutionExporter.InsecureLocalhostHttpClientName, factory.LastClientName);
    }

    [Fact]
    public async Task ExportAsync_WhenServerReturnsError_ReturnsHttpError()
    {
        var slnDir = CreateSampleSolutionDir("ExportFail", """{"GeneratorEndpoint":"https://export.local:5443"}""");
        var recordingHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom", Encoding.UTF8, "text/plain")
            });
        var exporter = new SolutionExporter(
            new SolutionLocator(),
            new JsonConfigStore(userConfigDir: Path.Combine(_tempRoot, "empty-global")),
            new FakeHttpClientFactory(recordingHandler));

        var result = await exporter.ExportAsync(slnDir);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.HTTP_ERROR, result.Error?.Code);
    }

    [Fact]
    public async Task ExportAsync_WhenManifestMissing_ReturnsFailure()
    {
        var slnDir = Path.Combine(_tempRoot, "NoManifest");
        Directory.CreateDirectory(slnDir);
        File.WriteAllText(Path.Combine(slnDir, "NoManifest.sln"), string.Empty);

        var exporter = new SolutionExporter(
            new SolutionLocator(),
            new JsonConfigStore(userConfigDir: Path.Combine(_tempRoot, "empty-global")),
            new FakeHttpClientFactory(new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));

        var result = await exporter.ExportAsync(slnDir);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.IO_ERROR, result.Error?.Code);
    }

    [Fact]
    public async Task ExportAsync_WhenCancelled_ReturnsCancelled()
    {
        var slnDir = CreateSampleSolutionDir("CancelledExport", null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exporter = new SolutionExporter(
            new SolutionLocator(),
            new JsonConfigStore(userConfigDir: Path.Combine(_tempRoot, "empty-global")),
            new FakeHttpClientFactory(new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));

        var result = await exporter.ExportAsync(slnDir, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CANCELLED, result.Error?.Code);
    }

    [Fact]
    public async Task ExportAsync_WhenHttpRequestTimesOut_ReturnsHttpError()
    {
        var slnDir = CreateSampleSolutionDir("TimeoutExport", null);
        var timeoutHandler = new RecordingHttpMessageHandler(_ =>
            throw new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout.",
                new TimeoutException()));

        var exporter = new SolutionExporter(
            new SolutionLocator(),
            new JsonConfigStore(userConfigDir: Path.Combine(_tempRoot, "empty-global")),
            new FakeHttpClientFactory(timeoutHandler));

        var result = await exporter.ExportAsync(slnDir);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.HTTP_ERROR, result.Error?.Code);
    }

    private string CreateSampleSolutionDir(string name, string? configJson)
    {
        var slnDir = Path.Combine(_tempRoot, name);
        var mmaDir = Path.Combine(slnDir, ".mma");
        Directory.CreateDirectory(mmaDir);
        File.WriteAllText(Path.Combine(slnDir, $"{name}.sln"), string.Empty);
        File.WriteAllText(
            Path.Combine(mmaDir, "project.mma"),
            $$"""{"Project":{"Name":"{{name}}","Path":"{{slnDir.Replace("\\", "\\\\")}}"},"Entities":[],"Rows":[],"Relations":[]}""");

        if (configJson is not null)
        {
            File.WriteAllText(Path.Combine(mmaDir, "config.json"), configJson);
        }

        return slnDir;
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public string? LastClientName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            LastClientName = name;
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return responder(request);
        }
    }
}

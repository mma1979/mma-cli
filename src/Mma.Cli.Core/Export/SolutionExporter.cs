using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Config;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Services;
using Mma.Cli.Core.Solutions;

namespace Mma.Cli.Core.Export;

public sealed class SolutionExporter : ISolutionExporter
{
    public const string HttpClientName = "MmaSolutionExporter";
    public const string InsecureLocalhostHttpClientName = "MmaSolutionExporterInsecureLocalhost";

    private const string DefaultGeneratorEndpoint = "https://localhost:44357";

    private readonly ISolutionLocator _solutionLocator;
    private readonly IConfigStore _configStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SolutionExporter>? _logger;

    public SolutionExporter(
        ISolutionLocator solutionLocator,
        IConfigStore configStore,
        IHttpClientFactory httpClientFactory,
        ILogger<SolutionExporter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(solutionLocator);
        ArgumentNullException.ThrowIfNull(configStore);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _solutionLocator = solutionLocator;
        _configStore = configStore;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public static HttpMessageHandler CreateHttpMessageHandler(string baseUrl, bool allowInsecureLocalhostTls)
    {
        if (IsInsecureLocalhostAllowed(baseUrl, allowInsecureLocalhostTls))
        {
            return new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                AllowAutoRedirect = false
            };
        }

        return new HttpClientHandler();
    }

    private static bool IsInsecureLocalhostAllowed(string baseUrl, bool allowInsecureLocalhostTls) =>
        allowInsecureLocalhostTls
        && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && uri.IsLoopback;

    public async Task<OperationResult> ExportAsync(string solutionPath, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return OperationResult.Failure(ErrorCodes.CANCELLED, "Solution export was cancelled.");
        }

        var located = _solutionLocator.Locate(solutionPath);
        if (!located.IsSuccess || located.Value is null)
        {
            return OperationResult.Failure(
                located.Error ?? new GenerationError(ErrorCodes.SOLUTION_NOT_FOUND, "Solution could not be located."));
        }

        var manifestPath = Path.Combine(located.Value.SolutionPath, ".mma", "project.mma");
        if (!File.Exists(manifestPath))
        {
            return OperationResult.Failure(
                ErrorCodes.IO_ERROR,
                $"Project manifest not found at '{manifestPath}'.");
        }

        string jsonPayload;
        try
        {
            var fileBytes = await ServiceFileHelper.ReadFileBytesSharedAsync(manifestPath, ct, maxSharingRetries: 4)
                .ConfigureAwait(false);
            var jsonMemory = ServiceFileHelper.StripUtf8Bom(fileBytes);
            using var doc = JsonDocument.Parse(jsonMemory);
            jsonPayload = Encoding.UTF8.GetString(jsonMemory.Span);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure(ErrorCodes.CANCELLED, "Solution export was cancelled.");
        }
        catch (JsonException ex)
        {
            return OperationResult.Failure(
                ErrorCodes.MANIFEST_INVALID,
                $"Invalid JSON in project manifest '{manifestPath}'.",
                ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                ErrorCodes.IO_ERROR,
                $"Failed to read project manifest '{manifestPath}'.",
                ex.Message);
        }

        var baseUrl = _configStore.GetSetting(located.Value.SolutionPath, "GeneratorEndpoint", DefaultGeneratorEndpoint);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return OperationResult.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid GeneratorEndpoint URL: '{baseUrl}'.");
        }

        var allowInsecure = string.Equals(
            _configStore.GetSetting(located.Value.SolutionPath, "AllowInsecureLocalhostTls", "false"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var normalizedBaseUri = baseUrl.EndsWith('/') ? baseUri : new Uri($"{baseUrl}/");
        var importUri = new Uri(normalizedBaseUri, "api/projects/import");
        var clientName = IsInsecureLocalhostAllowed(baseUrl, allowInsecure)
            ? InsecureLocalhostHttpClientName
            : HttpClientName;

        try
        {
            using var client = _httpClientFactory.CreateClient(clientName);
            using var content = new StringContent(jsonPayload, ServiceFileHelper.Utf8NoBom, "application/json");
            using var response = await client.PostAsync(importUri, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger?.LogWarning(
                    "Export POST to '{Endpoint}' failed with status {StatusCode}: {Body}",
                    importUri,
                    (int)response.StatusCode,
                    responseBody);
                return OperationResult.Failure(
                    ErrorCodes.HTTP_ERROR,
                    $"Export failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    responseBody);
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OperationResult.Failure(ErrorCodes.CANCELLED, "Solution export was cancelled.");
        }
        catch (OperationCanceledException ex)
        {
            _logger?.LogError(ex, "HTTP request timed out exporting solution to '{Endpoint}'.", importUri);
            return OperationResult.Failure(
                ErrorCodes.HTTP_ERROR,
                $"HTTP request timed out exporting solution: {ex.Message}",
                ex.ToString());
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error exporting solution to '{Endpoint}'.", importUri);
            return OperationResult.Failure(
                ErrorCodes.HTTP_ERROR,
                $"HTTP error exporting solution: {ex.Message}",
                ex.ToString());
        }
    }
}

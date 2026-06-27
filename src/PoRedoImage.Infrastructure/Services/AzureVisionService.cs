using System.Diagnostics;
using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Azure Computer Vision implementation of IVisionService.
/// Adapter pattern (GoF): adapts the Azure SDK to the domain interface.
/// </summary>
public sealed class AzureVisionService : IVisionService
{
    private readonly ILogger<AzureVisionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ImageAnalysisClient? _client;
    private readonly AzureKeyCredential? _credential;
    private readonly float _minTagConfidence;
    private readonly string? _configurationError;

    private string? CurrentKey => _configuration["ComputerVision:ApiKey"] ?? _configuration["ComputerVision:Key"];

    public AzureVisionService(IConfiguration configuration, ILogger<AzureVisionService> logger)
    {
        _logger = logger;
        _configuration = configuration;

        var endpoint = configuration["ComputerVision:Endpoint"];
        var key = CurrentKey;
        _minTagConfidence = configuration.GetValue<float>("ComputerVision:MinTagConfidence", 0.6f);

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            _configurationError = "ComputerVision:Endpoint and ComputerVision:ApiKey are not configured.";
            _logger.LogWarning("Azure Vision Service not configured: {Error}", _configurationError);
            return;
        }

        _credential = new AzureKeyCredential(key);
        // Explicit resilience (§3): exponential-backoff retries for transient 429/5xx/timeout failures.
        var options = new ImageAnalysisClientOptions
        {
            Retry =
            {
                MaxRetries = 3,
                Mode = Azure.Core.RetryMode.Exponential,
                Delay = TimeSpan.FromSeconds(1),
                NetworkTimeout = TimeSpan.FromSeconds(100),
            }
        };
        _client = new ImageAnalysisClient(new Uri(endpoint), _credential, options);
        _logger.LogInformation("Azure Vision Service initialized with endpoint: {Endpoint}", endpoint);
    }

    public async Task<(string Description, IReadOnlyList<string> Tags, double ConfidenceScore, long ElapsedMs)>
        AnalyzeAsync(byte[] imageData, CancellationToken ct = default)
    {
        if (_configurationError is not null)
            throw new InvalidOperationException(_configurationError);

        ArgumentNullException.ThrowIfNull(imageData);
        if (imageData.Length == 0)
            throw new ArgumentException("Image data cannot be empty", nameof(imageData));

        // Re-read key from IConfiguration to pick up Key Vault rotated secrets (singleton lifetime)
        if (!string.IsNullOrWhiteSpace(CurrentKey) && _credential is not null)
            _credential.Update(CurrentKey);

        _logger.LogInformation("Analyzing image via Azure Computer Vision. Size={Size} bytes", imageData.Length);
        var start = Stopwatch.GetTimestamp();

        // Try with Caption first. Caption is only available in certain Azure regions (e.g. eastus, westeurope).
        // If the endpoint's region doesn't support it, fall back to Tags-only and build a synthetic description.
        try
        {
            var response = await _client!.AnalyzeAsync(
                BinaryData.FromBytes(imageData),
                VisualFeatures.Caption | VisualFeatures.Tags,
                new ImageAnalysisOptions { Language = "en", GenderNeutralCaption = true }, ct);

            var description = response.Value.Caption?.Text ?? "No description available";
            var confidence = response.Value.Caption?.Confidence ?? 0;
            var tags = response.Value.Tags?.Values
                .Where(t => t.Confidence >= _minTagConfidence)
                .Select(t => t.Name)
                .ToList() ?? [];

            var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _logger.LogInformation("Vision analysis complete in {Elapsed}ms (with caption)", elapsed);
            return (description, tags.AsReadOnly(), confidence, elapsed);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 400 && ex.Message.Contains("Caption"))
        {
            _logger.LogWarning("Caption not supported in this region — retrying with Tags only");

            var response = await _client!.AnalyzeAsync(
                BinaryData.FromBytes(imageData),
                VisualFeatures.Tags,
                new ImageAnalysisOptions { Language = "en" }, ct);

            var tags = response.Value.Tags?.Values
                .Where(t => t.Confidence >= _minTagConfidence)
                .Select(t => t.Name)
                .ToList() ?? [];

            // Build a minimal description from top tags for downstream GPT enhancement
            var description = tags.Count > 0
                ? $"A photo showing {string.Join(", ", tags.Take(8))}"
                : "No description available";

            var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _logger.LogInformation("Vision analysis complete in {Elapsed}ms (tags-only fallback)", elapsed);
            return (description, tags.AsReadOnly(), 0, elapsed);
        }
    }
}

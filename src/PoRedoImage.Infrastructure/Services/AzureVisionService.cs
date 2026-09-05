using System.Diagnostics;
using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Application.Configuration;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

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

    /// <summary>
    /// Set once the service has learned that this resource's region does not serve the Caption
    /// feature, so the guaranteed-to-400 request is never sent again for the process lifetime.
    /// </summary>
    /// <remarks>
    /// Caption availability is a property of the deployed region, not of the image, so the answer
    /// cannot change between calls. Before this flag existed every single analysis sent two
    /// requests: a Caption|Tags call that always failed with 400, then the Tags-only retry that
    /// actually produced the result — doubling both latency and the per-transaction Vision bill on
    /// the app's primary code path, forever. Plain <c>volatile bool</c> rather than a lock: the
    /// worst a race can do is let two concurrent first-calls each discover the 400 independently,
    /// which is exactly the old behaviour and self-corrects on the next call.
    /// </remarks>
    private volatile bool _captionUnsupported;

    private string? CurrentKey => _configuration[ConfigKeys.ComputerVisionApiKey] ?? _configuration[ConfigKeys.ComputerVisionKeyLegacy];

    public AzureVisionService(IConfiguration configuration, ILogger<AzureVisionService> logger)
    {
        _logger = logger;
        _configuration = configuration;

        var endpoint = configuration[ConfigKeys.ComputerVisionEndpoint];
        var key = CurrentKey;
        _minTagConfidence = ConfigValue.Float(configuration, ConfigKeys.ComputerVisionMinTagConfidence, 0.6f);

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

    public async Task<(string Description, IReadOnlyList<string> Tags, double ConfidenceScore, long ElapsedMs, string? FallbackReason)>
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

        _logger.VisionAnalysisStarting(imageData.Length);
        var start = Stopwatch.GetTimestamp();

        // Caption is only available in certain Azure regions (e.g. eastus, westeurope). When this
        // resource's region does not serve it, the SDK returns 400 and we fall back to Tags-only.
        // That discovery is cached in _captionUnsupported so the failing call is made at most once.
        if (!_captionUnsupported)
        {
            try
            {
                var response = await _client!.AnalyzeAsync(
                    BinaryData.FromBytes(imageData),
                    VisualFeatures.Caption | VisualFeatures.Tags,
                    new ImageAnalysisOptions { Language = "en", GenderNeutralCaption = true }, ct);

                var caption = response.Value.Caption?.Text;
                var tags = ExtractTags(response.Value);
                var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                _logger.VisionAnalysisComplete(elapsed);

                // A 200 with no caption is not a region failure, so it must not set the flag — but
                // it is still a degraded result and the caller has to be able to say so.
                if (string.IsNullOrWhiteSpace(caption))
                    return (SynthesiseDescription(tags), tags, 0, elapsed, NoCaptionReason);

                return (caption, tags, response.Value.Caption?.Confidence ?? 0, elapsed, null);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 400 && ex.Message.Contains("Caption"))
            {
                _logger.VisionCaptionUnsupported();
                _captionUnsupported = true;
            }
        }

        // Tags-only path: either we already knew Caption was unavailable, or we just found out.
        var tagsOnlyResponse = await _client!.AnalyzeAsync(
            BinaryData.FromBytes(imageData),
            VisualFeatures.Tags,
            new ImageAnalysisOptions { Language = "en" }, ct);

        var tagsOnly = ExtractTags(tagsOnlyResponse.Value);
        var tagsOnlyElapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.VisionAnalysisCompleteTagsOnly(tagsOnlyElapsed);

        return (SynthesiseDescription(tagsOnly), tagsOnly, 0, tagsOnlyElapsed, CaptionUnsupportedReason);
    }

    /// <summary>
    /// Reason surfaced to the user when the region cannot caption images. Every fallback path in
    /// this codebase has to name itself: an unexplained tag-derived description reads to the user
    /// as "the AI ignored my photo".
    /// </summary>
    internal const string CaptionUnsupportedReason =
        "Azure Computer Vision in this region can't caption images, so the description was built "
        + "from detected tags instead of a look at the photo.";

    /// <summary>As above, for a 200 response that carried no caption.</summary>
    internal const string NoCaptionReason =
        "Azure Computer Vision returned no caption for this image, so the description was built "
        + "from detected tags instead.";

    private IReadOnlyList<string> ExtractTags(ImageAnalysisResult result) =>
        result.Tags?.Values
            .Where(t => t.Confidence >= _minTagConfidence)
            .Select(t => t.Name)
            .ToList()
            .AsReadOnly()
        ?? (IReadOnlyList<string>)[];

    /// <summary>Builds a minimal description from top tags for downstream GPT enhancement.</summary>
    private static string SynthesiseDescription(IReadOnlyList<string> tags) =>
        tags.Count > 0
            ? $"A photo showing {string.Join(", ", tags.Take(8))}"
            : "No description available";
}

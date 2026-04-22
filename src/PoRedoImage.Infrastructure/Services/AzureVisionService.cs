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
    private readonly ImageAnalysisClient? _client;
    private readonly float _minTagConfidence;
    private readonly string? _configurationError;

    public AzureVisionService(IConfiguration configuration, ILogger<AzureVisionService> logger)
    {
        _logger = logger;

        var endpoint = configuration["ComputerVision:Endpoint"];
        var key = configuration["ComputerVision:ApiKey"] ?? configuration["ComputerVision:Key"];
        _minTagConfidence = configuration.GetValue<float>("ComputerVision:MinTagConfidence", 0.6f);

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            _configurationError = "ComputerVision:Endpoint and ComputerVision:ApiKey are not configured.";
            _logger.LogWarning("Azure Vision Service not configured: {Error}", _configurationError);
            return;
        }

        _client = new ImageAnalysisClient(new Uri(endpoint), new AzureKeyCredential(key));
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

        _logger.LogInformation("Analyzing image via Azure Computer Vision. Size={Size} bytes", imageData.Length);
        var start = Stopwatch.GetTimestamp();

        var visualFeatures = VisualFeatures.Caption | VisualFeatures.Tags;
        var response = await _client!.AnalyzeAsync(
            BinaryData.FromBytes(imageData), visualFeatures,
            new ImageAnalysisOptions { Language = "en", GenderNeutralCaption = true }, ct);

        var description = response.Value.Caption?.Text ?? "No description available";
        var confidence = response.Value.Caption?.Confidence ?? 0;
        var tags = response.Value.Tags?.Values
            .Where(t => t.Confidence >= _minTagConfidence)
            .Select(t => t.Name)
            .ToList() ?? [];

        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Vision analysis complete in {Elapsed}ms", elapsed);
        return (description, tags.AsReadOnly(), confidence, elapsed);
    }
}

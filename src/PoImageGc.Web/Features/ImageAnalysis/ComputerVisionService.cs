using Azure;
using Azure.AI.Vision.ImageAnalysis;

namespace PoImageGc.Web.Features.ImageAnalysis;

/// <summary>
/// Interface for the Azure Computer Vision service
/// </summary>
public interface IComputerVisionService
{
    /// <summary>
    /// Analyzes an image and generates a description
    /// </summary>
    Task<(string Description, List<string> Tags, double ConfidenceScore, long ProcessingTimeMs)> AnalyzeImageAsync(byte[] imageData);
}

/// <summary>
/// Implementation of Computer Vision service using Azure AI Vision
/// </summary>
public class ComputerVisionService : IComputerVisionService
{
    private readonly ILogger<ComputerVisionService> _logger;
    private readonly ImageAnalysisClient _client;
    private readonly float _minTagConfidence;

    public ComputerVisionService(
        IConfiguration configuration,
        ILogger<ComputerVisionService> logger)
    {
        _logger = logger;

        var endpoint = configuration["ComputerVision:Endpoint"] ??
            throw new ArgumentNullException("ComputerVision:Endpoint is not configured");
        var key = configuration["ComputerVision:ApiKey"] ?? configuration["ComputerVision:Key"] ??
            throw new ArgumentNullException("ComputerVision:ApiKey or ComputerVision:Key is not configured");
        _minTagConfidence = configuration.GetValue<float>("ComputerVision:MinTagConfidence", 0.6f);

        // Cache the client — reuses HTTP connections across all requests (avoids socket exhaustion)
        _client = new ImageAnalysisClient(new Uri(endpoint), new AzureKeyCredential(key));

        _logger.LogInformation("Computer Vision Service initialized with endpoint: {Endpoint}", endpoint);
    }

    public async Task<(string Description, List<string> Tags, double ConfidenceScore, long ProcessingTimeMs)> AnalyzeImageAsync(byte[] imageData)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        if (imageData.Length == 0)
            throw new ArgumentException("Image data cannot be empty", nameof(imageData));

        _logger.LogInformation("Starting image analysis with Azure Computer Vision. Size: {Size} bytes", imageData.Length);
        var startTime = DateTime.UtcNow;

        try
        {
            // Caption feature requires specific regions: East US, France Central, Korea Central,
            // North Europe, Southeast Asia, West Europe, West US
            var visualFeatures = VisualFeatures.Caption | VisualFeatures.Tags;

            var response = await _client.AnalyzeAsync(
                BinaryData.FromBytes(imageData),
                visualFeatures,
                new ImageAnalysisOptions { Language = "en", GenderNeutralCaption = true });

            var description = response.Value.Caption?.Text ?? "No description available";
            var confidenceScore = response.Value.Caption?.Confidence ?? 0;

            var tags = new List<string>();
            if (response.Value.Tags != null)
            {
                foreach (var tag in response.Value.Tags.Values)
                {
                    if (tag.Confidence >= _minTagConfidence)
                    {
                        tags.Add(tag.Name);
                    }
                }
            }

            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("Image analysis completed in {ProcessingTime}ms. Tags: {TagCount}, Confidence: {Confidence:F2}",
                processingTime, tags.Count, confidenceScore);

            return (description, tags, confidenceScore, processingTime);
        }
        catch (Exception ex)
        {
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Error analyzing image after {ProcessingTime}ms", processingTime);
            throw;
        }
    }
}

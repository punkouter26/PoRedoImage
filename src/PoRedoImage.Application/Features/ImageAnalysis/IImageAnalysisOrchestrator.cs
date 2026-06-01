using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.ImageAnalysis;

/// <summary>
/// Orchestrates the image analysis pipeline (Analyze → Enhance → Generate).
/// Single Responsibility Principle (SOLID-S): coordinates domain services without knowing their implementations.
/// Open/Closed Principle (SOLID-O): new modes can be added without changing existing pipeline logic.
/// </summary>
public interface IImageAnalysisOrchestrator
{
    Task<ImageAnalysisResponse> ProcessAsync(ImageAnalysisRequest request, CancellationToken ct = default);
}

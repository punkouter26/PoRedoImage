using Microsoft.Extensions.Logging;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.ImageAnalysis;

/// <summary>
/// Source-generated logging (§4) for the image-analysis hot path. <c>[LoggerMessage]</c>
/// compiles these to allocation-free, zero-boxing log methods — no params array, no string
/// formatting unless the level is enabled.
/// </summary>
internal static partial class OrchestratorLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting image analysis pipeline. Mode={Mode}")]
    public static partial void PipelineStarting(this ILogger logger, ProcessingMode mode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Image analysis pipeline complete. TotalMs={TotalMs}")]
    public static partial void PipelineComplete(this ILogger logger, long totalMs);
}

using System.Text.Json.Serialization;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Imaging;

namespace PoRedoImage.Shared.Json;

/// <summary>
/// System.Text.Json source-generated serialization metadata for the DTOs that cross the
/// wire between the Blazor WASM client and the BFF. Source generation eliminates the
/// reflection-based codepath (which the trim/AOT analyzer flags as IL2026/IL3050) and
/// makes the JSON surface fully trim-safe.
///
/// Wire in via <c>JsonSerializerOptions.TypeInfoResolver = new SharedJsonContext()</c> in
/// both <c>PoRedoImage.Web.Program.cs</c> (server MVC JSON) and <c>PoRedoImage.Client.Program.cs</c>
/// (WASM <c>HttpClient</c> JSON). Add new DTOs here whenever a new endpoint is introduced.
/// </summary>
[JsonSerializable(typeof(ImageAnalysisRequest))]
[JsonSerializable(typeof(ImageAnalysisResponse))]
[JsonSerializable(typeof(ProcessingMetricsDto))]
[JsonSerializable(typeof(BulkDescribeRequest))]
[JsonSerializable(typeof(BulkDescribeResponse))]
[JsonSerializable(typeof(BulkVariationRequest))]
[JsonSerializable(typeof(BulkVariationResponse))]
[JsonSerializable(typeof(BulkGenerateImageResult))]
[JsonSerializable(typeof(SavePromptsRequest))]
[JsonSerializable(typeof(UserImageDto))]
[JsonSerializable(typeof(SaveOriginalRequest))]
[JsonSerializable(typeof(SaveResultRequest))]
[JsonSerializable(typeof(SaveImageResponse))]
[JsonSerializable(typeof(MemeTemplateDto))]
[JsonSerializable(typeof(MemeTemplateRenderRequest))]
[JsonSerializable(typeof(MemeTemplateRenderResponse))]
[JsonSerializable(typeof(StyleDirectorRequestDto))]
[JsonSerializable(typeof(StyleDirectorResultDto))]
[JsonSerializable(typeof(StyleDirectorReasoningEntryDto))]
[JsonSerializable(typeof(BulkRerollRequest))]
[JsonSerializable(typeof(BulkRerollResponse))]
[JsonSerializable(typeof(BulkRerollVariation))]
[JsonSerializable(typeof(ProblemDetailsDto))]
[JsonSerializable(typeof(AiPricingDto))]
[JsonSerializable(typeof(RapRoastRequest))]
[JsonSerializable(typeof(RapRoastResponse))]
[JsonSerializable(typeof(SceneSnapshotDto))]
[JsonSerializable(typeof(ClientVitalsSampleRequest))]
[JsonSerializable(typeof(ClientVitalsHistoryDto))]
[JsonSerializable(typeof(ClientVitalsPointDto))]
[JsonSerializable(typeof(ImageBytes))]
public partial class SharedJsonContext : JsonSerializerContext
{
}

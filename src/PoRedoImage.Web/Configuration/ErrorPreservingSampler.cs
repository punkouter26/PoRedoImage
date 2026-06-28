using OpenTelemetry.Trace;

namespace PoRedoImage.Web.Configuration;

/// <summary>
/// Head-based trace sampler that keeps routine telemetry cheap while never dropping error-bearing
/// spans. Normal spans are sampled at <c>ratio</c> (e.g. 10% in Production); any span that already
/// carries an error signal at start time — an <c>error=true</c> tag, or an HTTP status ≥ 500 — is
/// always kept. Wraps a <see cref="ParentBasedSampler"/> so a sampled-in trace keeps all of its
/// child spans instead of fragmenting a single operation across the sampling boundary.
///
/// Head sampling decides at span START, so a server request that throws LATER cannot be rescued
/// here. That case is covered by the unsampled Serilog → Application Insights sink (see Program.cs),
/// which exports every logged exception at 100%. This sampler is the trace-side complement: it
/// preserves trace integrity and keeps any span whose failure is known up front (most failed
/// outbound dependency calls), satisfying the "retain exceptions, drop noise" telemetry budget.
/// </summary>
public sealed class ErrorPreservingSampler : Sampler
{
    private readonly Sampler _inner;

    public ErrorPreservingSampler(double ratio)
    {
        _inner = new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio));
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        if (samplingParameters.Tags is not null && HasErrorSignal(samplingParameters.Tags))
        {
            return new SamplingResult(SamplingDecision.RecordAndSample);
        }

        return _inner.ShouldSample(in samplingParameters);
    }

    private static bool HasErrorSignal(IEnumerable<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            switch (tag.Key)
            {
                case "error" when tag.Value is true || (tag.Value is string s && s.Equals("true", StringComparison.OrdinalIgnoreCase)):
                    return true;
                // Both the new (otel) and legacy HTTP semantic-convention status-code keys.
                case "http.response.status_code" or "http.status_code":
                    if (int.TryParse(tag.Value?.ToString(), out var code) && code >= 500)
                        return true;
                    break;
            }
        }

        return false;
    }
}

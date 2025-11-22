# ADR 003: Implement OpenTelemetry for Observability

**Status:** Accepted  
**Date:** 2025-11-22  
**Deciders:** Development Team  

## Context

The application needs comprehensive observability for:
- Custom business metrics (image processing time, token usage, success rates)
- Distributed tracing across services
- Performance monitoring
- Compliance with modern observability standards

Application Insights provides telemetry, but lacks modern abstractions and vendor-neutral APIs.

## Decision

Implement **OpenTelemetry** as the primary abstraction for metrics and tracing, while continuing to use Application Insights as the backend.

### Implementation Details

1. **Metrics Collection:**
   ```csharp
   .WithMetrics(metrics => metrics
       .AddAspNetCoreInstrumentation()
       .AddHttpClientInstrumentation()
       .AddMeter("PoRedoImage.Api") // Custom metrics
       .AddOtlpExporter())
   ```

2. **Distributed Tracing:**
   ```csharp
   .WithTracing(tracing => tracing
       .AddAspNetCoreInstrumentation()
       .AddHttpClientInstrumentation()
       .AddOtlpExporter())
   ```

3. **Custom Metrics:**
   - Create `Meter` instances for business metrics
   - Track image processing duration
   - Monitor OpenAI token consumption
   - Measure success/failure rates

4. **Resource Attributes:**
   ```csharp
   .ConfigureResource(resource => resource
       .AddService("PoRedoImage")
       .AddAttributes(new Dictionary<string, object>
       {
           ["deployment.environment"] = environmentName,
           ["service.version"] = "1.0.0"
       }))
   ```

## Consequences

### Positive

- ✅ **Vendor Neutral:** Can switch backends without code changes
- ✅ **Industry Standard:** OpenTelemetry is CNCF standard
- ✅ **Rich Instrumentation:** Automatic ASP.NET Core and HttpClient metrics
- ✅ **Custom Metrics:** Easy to add business-specific metrics
- ✅ **Future Proof:** Standardized, actively developed
- ✅ **Multiple Exporters:** Console + OTLP + Application Insights

### Negative

- ❌ **Additional Packages:** Increases dependency count
- ❌ **Configuration Complexity:** More setup than basic telemetry
- ❌ **Package Vulnerability:** Current OpenTelemetry.Api has known moderate severity issue

### Mitigations

- Monitor for OpenTelemetry package updates
- Document all custom metrics in code
- Use descriptive meter and instrument names
- Regular dependency vulnerability scans

## Custom Metrics Strategy

### Business Metrics to Track

1. **Image Processing:**
   - `poredoimage.processing.duration` (Histogram)
   - `poredoimage.processing.count` (Counter)
   - `poredoimage.processing.failures` (Counter)

2. **AI Service Usage:**
   - `poredoimage.openai.tokens.total` (Counter)
   - `poredoimage.openai.tokens.prompt` (Counter)
   - `poredoimage.openai.tokens.completion` (Counter)
   - `poredoimage.vision.api.calls` (Counter)

3. **Performance:**
   - `poredoimage.image.upload.size` (Histogram)
   - `poredoimage.description.length` (Histogram)

## Alternatives Considered

### 1. Application Insights Only
**Rejected:** Vendor lock-in, limited custom metric capabilities

### 2. Prometheus Client Library
**Rejected:** Not native to .NET ecosystem, requires separate infrastructure

### 3. Custom Metrics Framework
**Rejected:** Reinventing the wheel, no standard format

## References

- [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/instrumentation/net/)
- [OpenTelemetry Metrics API](https://opentelemetry.io/docs/specs/otel/metrics/api/)
- [Azure Monitor OpenTelemetry](https://learn.microsoft.com/azure/azure-monitor/app/opentelemetry-enable)

## Related Decisions

- ADR 004: Continue Using Serilog for Structured Logging
- ADR 005: Export Metrics to Both Console and OTLP

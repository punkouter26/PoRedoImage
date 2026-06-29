using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PoRedoImage.Infrastructure.Services.Mocks;

/// <summary>
/// Defense-in-depth budget guardrail. When <c>Mocks:UseMockAi=true</c>, the DI container already
/// swaps the high-cost AI services for zero-network mocks (see
/// <c>InfrastructureServiceExtensions.AddPoRedoImageInfrastructure</c>), so in practice the real
/// named HTTP clients are never resolved. This handler sits on those clients anyway as a second,
/// HTTP-pipeline-level wall: if a future regression wires a real service while mock mode is on, any
/// outbound AI call is blocked here instead of silently spending a live token.
///
/// It fails LOUD (throws) rather than returning canned data — a thrown exception surfaces the
/// misconfiguration immediately, whereas a fake 200 would mask it and let "mocked" runs quietly
/// diverge from reality. When mock mode is off it is a transparent pass-through.
/// </summary>
public sealed class MockAiDelegatingHandler : DelegatingHandler
{
    private readonly bool _useMockAi;
    private readonly ILogger<MockAiDelegatingHandler> _logger;

    public MockAiDelegatingHandler(IConfiguration configuration, ILogger<MockAiDelegatingHandler> logger)
    {
        _useMockAi = configuration.GetValue<bool>("Mocks:UseMockAi");
        _logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_useMockAi)
        {
            _logger.LogError(
                "Mocks:UseMockAi is true but a real outbound AI HTTP call to {Uri} was attempted. "
                + "Blocking it to guarantee zero live token spend — a service was wired to a real client "
                + "while mock mode is on. Check the DI registration.", request.RequestUri);

            throw new InvalidOperationException(
                $"Outbound AI call to '{request.RequestUri}' blocked: Mocks:UseMockAi is enabled, so no "
                + "live AI token may be spent. The AI services should be mock implementations in this mode.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}

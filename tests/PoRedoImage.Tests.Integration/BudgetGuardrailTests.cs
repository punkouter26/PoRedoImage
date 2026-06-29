using Microsoft.Extensions.DependencyInjection;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services.Mocks;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Budget-waste guardrails: proves the automated-test host wires the zero-network mock AI services
/// rather than the real Azure OpenAI / Computer Vision / Google Gemini clients, so the suite can
/// never spend a live token. If someone removes Mocks:UseMockAi from the test factory, these fail.
/// </summary>
public sealed class BudgetGuardrailTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BudgetGuardrailTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void Ai_services_are_mocks_not_live_clients()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.IsType<MockVisionService>(sp.GetRequiredService<IVisionService>());
        Assert.IsType<MockGenerativeAiService>(sp.GetRequiredService<IGenerativeAiService>());
        Assert.IsType<MockImagen3Service>(sp.GetRequiredService<IImageGenerationService>());
    }

    [Fact]
    public void Mockable_services_are_registered_for_the_banner()
    {
        using var scope = _factory.Services.CreateScope();
        var mocks = scope.ServiceProvider.GetServices<IMockable>().ToList();

        // All three AI boundaries should surface a banner reason.
        Assert.Equal(3, mocks.Count);
        Assert.All(mocks, m => Assert.False(string.IsNullOrWhiteSpace(m.MockReason)));
    }

    [Fact]
    public async Task Mock_image_generation_returns_canned_png_without_network()
    {
        using var scope = _factory.Services.CreateScope();
        var imagen = scope.ServiceProvider.GetRequiredService<IImageGenerationService>();

        var (bytes, contentType, _) = await imagen.GenerateAsync("anything");

        Assert.Equal("image/png", contentType);
        Assert.NotEmpty(bytes);
    }
}

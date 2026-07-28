using System.Net;
using System.Net.Http.Json;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Integration.Features;

/// <summary>
/// Endpoint-level coverage for the Rap Roast slice. Every AI collaborator is mocked by
/// <see cref="CustomWebApplicationFactory"/>, so these exercise routing, validation, auth, and
/// serialization without any possibility of live token spend.
/// </summary>
public class RapRoastEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RapRoastEndpointTests(CustomWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task RapRoast_ValidImage_Returns200WithLyrics()
    {
        // PNG magic bytes — ImageBytes.FromBase64 validates these before anything expensive runs.
        var request = new RapRoastRequest
        {
            ImageData = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            ContentType = "image/png",
            Style = RapStyle.BoomBap,
        };

        var response = await _client.PostAsJsonAsync("/api/rap-roast", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RapRoastResponse>();
        Assert.NotNull(result);

        // Lyrics are the one part of the contract that is always populated, refusal or not.
        Assert.NotEmpty(result.Lyrics);
        Assert.Contains("[Verse]", result.Lyrics, StringComparison.Ordinal);
        Assert.NotEmpty(result.ImageDescription);
    }

    [Fact]
    public async Task RapRoast_EmptyImageData_Returns400()
    {
        var request = new RapRoastRequest { ImageData = "", ContentType = "image/png" };

        var response = await _client.PostAsJsonAsync("/api/rap-roast", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RapRoast_NonImageBytes_Returns400()
    {
        // Valid base64, but not an image — the magic-byte guard must reject it rather than
        // letting a text blob reach the vision backend.
        var request = new RapRoastRequest
        {
            ImageData = Convert.ToBase64String("this is definitely not a picture"u8.ToArray()),
            ContentType = "image/png",
        };

        var response = await _client.PostAsJsonAsync("/api/rap-roast", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

using System.Text.Json;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Json;

namespace PoRedoImage.Tests.Unit.Mobile;

public class MobileContractTests
{
    [Fact]
    public void ImageCaptureResult_roundtrips_cleanly()
    {
        var original = new ImageCaptureResult(
            "photo.jpg", "image/jpeg", [1, 2, 3, 4], "AQIDBA==", 4, 1280, 720);

        Assert.Equal("1280×720 px (4 B)", original.FormattedSummary);
        Assert.Equal("4 B", original.FormattedSize);
        Assert.Equal(1280, original.Width);
        Assert.Equal(720, original.Height);
    }

    [Fact]
    public void ImageAnalysisRequest_with_meme_mode_roundtrips()
    {
        var request = new ImageAnalysisRequest
        {
            ImageData = "AQIDBA==",
            ContentType = "image/jpeg",
            FileName = "snap.jpg",
            Mode = ProcessingMode.MemeGeneration,
            DescriptionLength = 200
        };

        var json = JsonSerializer.Serialize(request, SharedJsonOptions.Default);
        var roundtripped = JsonSerializer.Deserialize<ImageAnalysisRequest>(json, SharedJsonOptions.Default);

        Assert.NotNull(roundtripped);
        Assert.Equal(request.ImageData, roundtripped.ImageData);
        Assert.Equal(request.ContentType, roundtripped.ContentType);
        Assert.Equal(ProcessingMode.MemeGeneration, roundtripped.Mode);
        Assert.Equal(200, roundtripped.DescriptionLength);
    }

    [Fact]
    public void RapRoastRequest_roundtrips_with_style_and_intensity()
    {
        var request = new RapRoastRequest
        {
            ImageData = "AQIDBA==",
            ContentType = "image/jpeg",
            Style = RapStyle.Trap,
            Intensity = RoastIntensity.Scorched
        };

        var json = JsonSerializer.Serialize(request, SharedJsonOptions.Default);
        var roundtripped = JsonSerializer.Deserialize<RapRoastRequest>(json, SharedJsonOptions.Default);

        Assert.NotNull(roundtripped);
        Assert.Equal(request.ImageData, roundtripped.ImageData);
        Assert.Equal(RapStyle.Trap, roundtripped.Style);
        Assert.Equal(RoastIntensity.Scorched, roundtripped.Intensity);
    }
}


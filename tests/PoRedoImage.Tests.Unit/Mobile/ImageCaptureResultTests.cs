using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Unit.Mobile;

public class ImageCaptureResultTests
{
    [Fact]
    public void FormattedSize_formats_bytes_correctly()
    {
        var small = new ImageCaptureResult("test.jpg", "image/jpeg", [1, 2, 3], "AQID", 500);
        Assert.Equal("500 B", small.FormattedSize);

        var kb = new ImageCaptureResult("test.jpg", "image/jpeg", [1, 2, 3], "AQID", 512 * 1024);
        Assert.Equal("512.0 KB", kb.FormattedSize);

        var mb = new ImageCaptureResult("test.jpg", "image/jpeg", [1, 2, 3], "AQID", 2 * 1024 * 1024);
        Assert.Equal("2.0 MB", mb.FormattedSize);
    }

    [Fact]
    public void FormattedSummary_includes_dimensions_when_available()
    {
        var withDims = new ImageCaptureResult("test.jpg", "image/jpeg", [1, 2, 3], "AQID", 200 * 1024, 1920, 1080);
        Assert.Equal("1920×1080 px (200.0 KB)", withDims.FormattedSummary);

        var withoutDims = new ImageCaptureResult("test.jpg", "image/jpeg", [1, 2, 3], "AQID", 200 * 1024);
        Assert.Equal("200.0 KB", withoutDims.FormattedSummary);
    }
}


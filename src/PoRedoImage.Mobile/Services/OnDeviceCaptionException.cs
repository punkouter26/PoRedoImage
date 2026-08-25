namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Thrown when on-device caption generation cannot run or fails partway.
/// </summary>
/// <remarks>
/// Mirrors <c>LocalInferenceException</c> on the web client, and for the same reason: when the user
/// has explicitly chosen a free on-device model, quietly re-running the work against the metered
/// server is a surprise charge. This surfaces verbatim instead. Messages are written to be read by
/// the user, not just logged — they say what to do next.
/// </remarks>
public sealed class OnDeviceCaptionException : Exception
{
    public OnDeviceCaptionException(string message) : base(message)
    {
    }

    public OnDeviceCaptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

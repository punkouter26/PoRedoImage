using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Strongly-typed wrapper for an uploaded image. Eliminates the repeated
/// <c>try { Convert.FromBase64String(input) }</c> + magic-byte sniffs that
/// were scattered across 7 endpoint files (R1 in Po2Logic refactor queue).
/// Constructed at the JSON boundary via <see cref="JsonConverter{T}"/>; subsequent
/// layers receive a validated, immutable value object that has already been
/// checked for size, content-type, and magic bytes.
/// <para>
/// Memory: stores raw bytes only — no redundant base64 string kept around. To
/// re-emit, call <see cref="ToBase64String"/> which writes to a pooled buffer.
/// </para>
/// </summary>
[JsonConverter(typeof(ImageBytesJsonConverter))]
public readonly record struct ImageBytes
{
    public const int MaxSizeBytes = 20 * 1024 * 1024; // 20 MB — matches client-side ImageLoadHelper

    public ReadOnlyMemory<byte> Bytes { get; }
    public string ContentType { get; }

    private ImageBytes(ReadOnlyMemory<byte> bytes, string contentType)
    {
        Bytes = bytes;
        ContentType = contentType;
    }

    /// <summary>Wraps pre-validated bytes (no re-validation). Use only at the trust boundary.</summary>
    public static ImageBytes FromTrusted(ReadOnlyMemory<byte> bytes, string contentType) =>
        new(bytes, contentType);

    /// <summary>Factory that validates magic bytes + content-type. Throws on invalid input.</summary>
    public static ImageBytes Create(ReadOnlySpan<byte> bytes, string contentType)
    {
        if (bytes.Length > MaxSizeBytes)
            throw new ImageValidationException(
                $"Image exceeds the maximum allowed size of {MaxSizeBytes / 1024 / 1024} MB. " +
                $"Provided: {Math.Round(bytes.Length / 1024.0 / 1024.0, 2)} MB.");

        var resolvedType = ResolveContentType(bytes, contentType);
        return new ImageBytes(bytes.ToArray(), resolvedType);
    }

    /// <summary>Decodes a base64 string and validates the resulting bytes.</summary>
    public static ImageBytes FromBase64(string base64, string? contentTypeHint = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(base64);

        // Strip optional "data:<mime>;base64," prefix without allocating a substring slice.
        ReadOnlySpan<char> chars = base64;
        if (chars.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = chars.IndexOf(',');
            if (comma < 0)
                throw new ImageValidationException("Image data URI is malformed (no comma).");
            chars = chars[(comma + 1)..];
        }

        // Base64 decoding is bounded by input size; allocate exact-size destination.
        var buffer = GC.AllocateUninitializedArray<byte>(Base64.GetMaxDecodedFromUtf8Length(chars.Length * sizeof(char)));
        if (!Convert.TryFromBase64Chars(chars, buffer, out var written) || written <= 0)
            throw new ImageValidationException("Image data is not valid base64.");

        var detectedType = ResolveContentType(buffer.AsSpan(0, written), contentTypeHint);
        if (written > MaxSizeBytes)
            throw new ImageValidationException(
                $"Decoded image exceeds the maximum allowed size of {MaxSizeBytes / 1024 / 1024} MB.");

        return new ImageBytes(buffer.AsMemory(0, written), detectedType);
    }

    /// <summary>Re-encodes as base64 into a pooled buffer; caller must <c>Return</c> the <see cref="IMemoryOwner{T}"/>.</summary>
    public IMemoryOwner<char> ToBase64(out int length)
    {
        var maxLen = Base64.GetMaxEncodedToUtf8Length(Bytes.Length);
        var pool = ArrayPool<byte>.Shared;
        var owner = pool.Rent(maxLen);
        var encodeStatus = Base64.EncodeToUtf8(Bytes.Span, owner, out var utf8Written, out _);
        if (encodeStatus != OperationStatus.Done)
        {
            pool.Return(owner);
            throw new InvalidOperationException("Failed to re-encode image as base64.");
        }

        // Convert UTF-8 → UTF-16 chars (ASP.NET wire format).
        var charCount = Encoding.UTF8.GetCharCount(owner, 0, utf8Written);
        var charPool = ArrayPool<char>.Shared;
        var charOwner = charPool.Rent(charCount);
        var actual = Encoding.UTF8.GetChars(owner, 0, utf8Written, charOwner, 0);
        pool.Return(owner);
        length = actual;
        return new CharArrayOwner(charOwner, charPool, actual);
    }

    public string ToBase64String() => Convert.ToBase64String(Bytes.Span);

    private static string ResolveContentType(ReadOnlySpan<byte> bytes, string? hint)
    {
        var detected = DetectFromMagicBytes(bytes);
        if (detected is null)
            throw new ImageValidationException(
                "Unsupported image format. Supported types: JPEG, PNG, GIF, WebP, BMP. " +
                "(On iOS, convert HEIC to JPG before uploading.)");

        // Trust the magic bytes over the content-type hint to defeat header spoofing.
        if (hint is { Length: > 0 } && !string.Equals(hint, detected, StringComparison.OrdinalIgnoreCase))
        {
            // Only allow matching — silently coerce mismatches to the detected type.
            return detected;
        }
        return detected;
    }

    private static string? DetectFromMagicBytes(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return "image/gif";
        if (b.Length >= 2 && b[0] == 0x42 && b[1] == 0x4D) return "image/bmp";
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
            b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return "image/webp";
        return null;
    }

    public override string ToString() => $"ImageBytes({Bytes.Length} bytes, {ContentType})";

    private sealed class CharArrayOwner : IMemoryOwner<char>
    {
        private readonly char[] _array;
        private readonly ArrayPool<char> _pool;
        public int Length { get; }
        public CharArrayOwner(char[] array, ArrayPool<char> pool, int length)
        {
            _array = array; _pool = pool; Length = length;
        }
        public Memory<char> Memory => _array.AsMemory(0, Length);
        public void Dispose() => _pool.Return(_array, clearArray: true);
    }
}

public sealed class ImageValidationException : Exception
{
    public ImageValidationException(string message) : base(message) { }
    public ImageValidationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// System.Text.Json converter that deserialises a string property into a validated
/// <see cref="ImageBytes"/>. The wire format is the same as the prior hand-rolled
/// base64 strings — no client contract change.
/// </summary>
public sealed class ImageBytesJsonConverter : JsonConverter<ImageBytes>
{
    public override ImageBytes Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            throw new ImageValidationException("Image data is required.");

        if (reader.TokenType != JsonTokenType.String)
            throw new ImageValidationException("Image data must be a base64 string.");

        var raw = reader.GetString() ?? throw new ImageValidationException("Image data is required.");
        return ImageBytes.FromBase64(raw);
    }

    public override void Write(Utf8JsonWriter writer, ImageBytes value, JsonSerializerOptions options)
    {
        // Write as base64 string for round-trip with the existing wire contract.
        writer.WriteStringValue(value.ToBase64String());
    }
}

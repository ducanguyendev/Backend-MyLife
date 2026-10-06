namespace MyLife.Features.Library.Services;

public static class LibraryUploadRules
{
    public const int MaximumPhotoBytes = 5 * 1024 * 1024;
    public const int MaximumFiles = 20;
    public const long MaximumRequestBytes = MaximumFiles * (long)MaximumPhotoBytes + 1024 * 1024;

    public static string NormalizeContentType(string value) => value.Trim().ToLowerInvariant() is "image/jpg"
        ? "image/jpeg" : value.Trim().ToLowerInvariant();

    public static string ExtensionFor(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg", "image/png" => ".png", "image/webp" => ".webp", "image/gif" => ".gif",
        _ => throw new LibraryOperationException(400, "Only JPG, PNG, WEBP and GIF images are supported.")
    };

    public static void ValidateFile(IFormFile file)
    {
        if (file.Length is <= 0 or > MaximumPhotoBytes)
            throw new LibraryOperationException(400, "Each photo must be between 1 byte and 5 MB.");
        var name = file.FileName;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl) || name.Contains('/') || name.Contains('\\'))
            throw new LibraryOperationException(400, "A photo filename must be a safe filename, up to 255 characters, without paths.");
        var mime = NormalizeContentType(file.ContentType);
        var expected = ExtensionFor(mime);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension != expected && !(mime == "image/jpeg" && extension == ".jpeg"))
            throw new LibraryOperationException(400, "Photo extension and MIME type must match.");
    }

    public static bool HasMatchingSignature(byte[] content, string contentType) => contentType switch
    {
        "image/jpeg" => content.Length >= 3 && content[0] == 0xff && content[1] == 0xd8 && content[2] == 0xff,
        "image/png" => content.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/webp" => content.Length >= 12 && content.AsSpan(0, 4).SequenceEqual("RIFF"u8) && content.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        "image/gif" => content.AsSpan().StartsWith("GIF87a"u8) || content.AsSpan().StartsWith("GIF89a"u8),
        _ => false
    };
}

public sealed class LibraryOperationException(int statusCode, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
}

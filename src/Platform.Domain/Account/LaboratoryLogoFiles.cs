using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Account;

/// <summary>A logo image accepted by <see cref="LaboratoryLogoFiles.Inspect"/>: the detected content type and the size.</summary>
public sealed record LogoFile(string ContentType, long SizeBytes);

/// <summary>
/// The content rules of a laboratory logo: PNG, JPEG or WebP decided by the file's signature (magic bytes) only — the
/// client's name and content type are ignored — and 1 byte to 512 KB. SVG is refused on purpose: it can carry script.
/// </summary>
public static class LaboratoryLogoFiles
{
    public const long MaxSizeBytes = 512L * 1024;

    /// <summary>Bytes of the head of a file the signature check needs (WebP: "RIFF" + size + "WEBP").</summary>
    public const int HeaderLength = 12;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] RiffSignature = "RIFF"u8.ToArray();
    private static readonly byte[] WebpSignature = "WEBP"u8.ToArray();

    /// <summary>The content type the signature of <paramref name="header"/> announces, or <see langword="null"/>.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(PngSignature))
        {
            return "image/png";
        }

        if (header.StartsWith(JpegSignature))
        {
            return "image/jpeg";
        }

        return header.Length >= HeaderLength && header.StartsWith(RiffSignature) && header[8..12].SequenceEqual(WebpSignature) ? "image/webp" : null;
    }

    /// <summary>Validates size and signature; returns the accepted image or refuses with ACC-040…042.</summary>
    public static LogoFile Inspect(long sizeBytes, ReadOnlySpan<byte> header)
    {
        if (sizeBytes <= 0)
        {
            throw new DomainException(AccountCodes.LogoEmpty, "The file is empty.", DomainFault.Validation);
        }

        if (sizeBytes > MaxSizeBytes)
        {
            throw new DomainException(AccountCodes.LogoTooLarge, $"A logo may not exceed {MaxSizeBytes / 1024} KB.", DomainFault.Validation);
        }

        var contentType = DetectContentType(header)
            ?? throw new DomainException(AccountCodes.LogoTypeNotAllowed, "The logo must be a PNG, JPEG or WebP image.", DomainFault.Validation);
        return new LogoFile(contentType, sizeBytes);
    }
}

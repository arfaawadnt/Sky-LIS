using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>The two images a report template prints (SRS CP-54 / SA-12; LDM §12 logo and signature image).</summary>
public enum ReportTemplateImage
{
    Logo = 0,
    Signature = 1,
}

/// <summary>
/// The content rules of a report-template image, the same as the laboratory logo's: PNG, JPEG or WebP decided by the
/// file's signature (magic bytes) only — the client's name and content type are ignored — and 1 byte to 512 KB. SVG is
/// refused on purpose: it can carry script. The bytes live in the Files module (<c>IFileStore</c>); the template keeps
/// the file id (intake LIS-INT-CTB-001 A10).
/// </summary>
public static class ReportTemplateImageFiles
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

    /// <summary>Validates size and signature; returns the detected content type or refuses with CAT-270…272.</summary>
    public static string Inspect(long sizeBytes, ReadOnlySpan<byte> header)
    {
        if (sizeBytes <= 0)
        {
            throw new DomainException(CatalogueBCodes.TemplateImageEmpty, "The file is empty.", DomainFault.Validation);
        }

        if (sizeBytes > MaxSizeBytes)
        {
            throw new DomainException(CatalogueBCodes.TemplateImageTooLarge, $"A template image may not exceed {MaxSizeBytes / 1024} KB.", DomainFault.Validation);
        }

        return DetectContentType(header)
            ?? throw new DomainException(CatalogueBCodes.TemplateImageTypeNotAllowed, "The image must be a PNG, JPEG or WebP file.", DomainFault.Validation);
    }
}

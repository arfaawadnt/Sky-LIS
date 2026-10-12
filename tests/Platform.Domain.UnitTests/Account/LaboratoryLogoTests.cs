using Platform.Domain.Account;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Account;

public sealed class LaboratoryLogoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static readonly byte[] Webp = [.. "RIFF"u8.ToArray(), 26, 0, 0, 0, .. "WEBP"u8.ToArray()];

    [Fact]
    public void The_signature_decides_the_type()
    {
        Assert.Equal("image/png", LaboratoryLogoFiles.Inspect(100, Png).ContentType);
        Assert.Equal("image/jpeg", LaboratoryLogoFiles.Inspect(100, Jpeg).ContentType);
        Assert.Equal("image/webp", LaboratoryLogoFiles.Inspect(100, Webp).ContentType);
        Assert.Null(LaboratoryLogoFiles.DetectContentType("<svg xmlns"u8));
        Assert.Null(LaboratoryLogoFiles.DetectContentType([.. "RIFF"u8.ToArray(), 26, 0, 0, 0, .. "WAVE"u8.ToArray()]));   // a RIFF that is not WebP
    }

    [Theory]
    [InlineData(0L, AccountCodes.LogoEmpty)]
    [InlineData(LaboratoryLogoFiles.MaxSizeBytes + 1, AccountCodes.LogoTooLarge)]
    public void Empty_or_too_large_files_are_refused(long size, string code) =>
        Assert.Equal(code, Assert.Throws<DomainException>(() => LaboratoryLogoFiles.Inspect(size, Png)).Code);

    [Fact]
    public void Other_formats_are_refused()
    {
        var refused = Assert.Throws<DomainException>(() => LaboratoryLogoFiles.Inspect(100, "%PDF-1.7"u8));
        Assert.Equal(AccountCodes.LogoTypeNotAllowed, refused.Code);
    }

    [Fact]
    public void Every_change_moves_the_version_and_a_removal_needs_a_logo()
    {
        var actor = Guid.CreateVersion7();
        var logo = LaboratoryLogo.Upload(new LogoFile("image/png", 100), "t/account/l/1", new string('A', 64), actor, Now);
        Assert.Equal((1, true, "image/png"), (logo.Version, logo.IsActive, logo.ContentType));
        Assert.Equal(new string('a', 64), logo.Sha256);
        Assert.IsType<LaboratoryLogoChanged>(Assert.Single(logo.DomainEvents));

        logo.Replace(new LogoFile("image/jpeg", 200), "t/account/l/2", new string('b', 64), actor, Now);
        Assert.Equal((2, "image/jpeg", 200L), (logo.Version, logo.ContentType, logo.SizeBytes));

        logo.Remove(actor, Now);
        Assert.Equal((3, false), (logo.Version, logo.IsActive));
        Assert.Equal(AccountCodes.LogoNotFound, Assert.Throws<DomainException>(() => logo.Remove(actor, Now)).Code);

        logo.Replace(new LogoFile("image/webp", 300), "t/account/l/3", new string('c', 64), actor, Now);
        Assert.Equal((4, true), (logo.Version, logo.IsActive));
    }
}

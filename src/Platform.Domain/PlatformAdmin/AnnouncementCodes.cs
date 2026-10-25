namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// Refusal codes of the announcements of National Technology and its partners (change request LIS-CR-013, C-17; intake
/// LIS-INT-PLT-001 §19). 001–099 validation and state, 404 not found.
/// </summary>
public static class AnnouncementCodes
{
    /// <summary>A title (both languages, ≤ 120) or a body (≤ 600) is missing or too long.</summary>
    public const string TextInvalid = "ANN-001";

    /// <summary>The link is not an absolute <c>https://</c> address of at most 500 characters.</summary>
    public const string LinkInvalid = "ANN-002";

    /// <summary>The schedule ends before it starts.</summary>
    public const string ScheduleInvalid = "ANN-003";

    /// <summary>The audience is malformed (an empty client id, more than 200 clients, an unknown status).</summary>
    public const string AudienceInvalid = "ANN-004";

    /// <summary>The placement is not <c>SignIn</c> or <c>Welcome</c> (the public list serves <c>SignIn</c> only).</summary>
    public const string PlacementInvalid = "ANN-005";

    public const string ImageEmpty = "ANN-006";
    public const string ImageTooLarge = "ANN-007";

    /// <summary>The image is not a PNG, JPEG or WebP by its signature (SVG is refused: it can carry script).</summary>
    public const string ImageTypeNotAllowed = "ANN-008";

    /// <summary>An announcement whose schedule has ended cannot be activated: extend the end first.</summary>
    public const string ScheduleEnded = "ANN-010";

    /// <summary>The announcement has no image to remove.</summary>
    public const string ImageMissing = "ANN-011";

    /// <summary>The audience names a country, a plan or a client the platform does not know.</summary>
    public const string AudienceCountryNotFound = "ANN-012";
    public const string AudiencePlanNotFound = "ANN-013";
    public const string AudienceClientNotFound = "ANN-014";

    public const string AnnouncementNotFound = "ANN-404";
}

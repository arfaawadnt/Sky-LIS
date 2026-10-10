namespace Platform.Domain.Results;

/// <summary>
/// Report units (LDM §2.6 "one printable report: tests grouped by clinical group; each mega profile is its own report"):
/// the key of a unit (its status is the lowest status of its live results, BR-04: <see cref="ResultStatuses.Lowest"/>).
/// </summary>
public static class ReportUnits
{
    /// <summary>The unit of a clinical group of a visit: <c>{visit}-{group}</c> (32-hex ids).</summary>
    public static string ForGroup(Guid visitId, Guid clinicalGroupId) => $"{visitId:N}-{clinicalGroupId:N}";

    /// <summary>The unit of a service printed alone on a line: <c>{visit}-{line}-{service}</c>.</summary>
    public static string ForService(Guid visitId, Guid lineId, Guid serviceId) => $"{visitId:N}-{lineId:N}-{serviceId:N}";

    /// <summary>The visit a key belongs to, or null when the key is not well formed.</summary>
    public static Guid? VisitOf(string? reportUnitKey)
    {
        if (string.IsNullOrWhiteSpace(reportUnitKey) || reportUnitKey.Length < 32)
        {
            return null;
        }

        return Guid.TryParseExact(reportUnitKey.AsSpan(0, 32), "N", out var visitId) ? visitId : null;
    }

    /// <summary>The service of a print-alone unit (the third part of the key), or null for a clinical-group unit.</summary>
    public static Guid? ServiceOf(string? reportUnitKey)
    {
        var parts = (reportUnitKey ?? string.Empty).Split('-');
        return parts.Length == 3 && Guid.TryParseExact(parts[2], "N", out var serviceId) ? serviceId : null;
    }

    /// <summary>True when the key has one of the two shapes (used to validate route values).</summary>
    public static bool IsWellFormed(string? reportUnitKey)
    {
        if (string.IsNullOrWhiteSpace(reportUnitKey))
        {
            return false;
        }

        var parts = reportUnitKey.Split('-');
        return parts.Length is 2 or 3 && parts.All(part => part.Length == 32 && Guid.TryParseExact(part, "N", out _));
    }
}

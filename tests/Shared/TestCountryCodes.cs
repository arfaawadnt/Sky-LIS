namespace Platform.TestSupport;

/// <summary>
/// Two-letter ISO codes for the countries the database tests create. Countries are platform-wide and unique by ISO code,
/// and a test process truncates the database once, so every code handed out must be new for the whole process: one
/// counter per process instead of a prefix per test class (which collided: "X?" of two classes, "J?" wrapping after 26
/// codes). The codes the seed migration or a test spells by hand are skipped, and so is the "U?" row of
/// <c>ReportTemplateImageFlowTests</c>. Shared by the integration and functional suites (linked source).
/// </summary>
internal static class TestCountryCodes
{
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal) { "AE", "EG", "JO", "KW", "SA", "ZB" };

    private static int _next = -1;

    /// <summary>A code no other caller in this process received; 650 per process.</summary>
    public static string Next()
    {
        while (true)
        {
            var n = Interlocked.Increment(ref _next);
            if (n >= 26 * 26)
            {
                throw new InvalidOperationException("The test process used every free two-letter country code.");
            }

            var code = new string([(char)('A' + (n / 26)), (char)('A' + (n % 26))]);
            if (code[0] != 'U' && !Reserved.Contains(code))
            {
                return code;
            }
        }
    }
}

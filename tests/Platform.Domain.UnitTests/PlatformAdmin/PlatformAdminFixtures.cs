using Platform.Domain.PlatformAdmin;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;

namespace Platform.Domain.UnitTests.PlatformAdmin;

/// <summary>Reference data shared by the platform administration tests (intake LIS-INT-PLT-001 §13 seed values).</summary>
internal static class PlatformAdminFixtures
{
    public static readonly DateTimeOffset Now = FixedClock.Instant;

    public static Country Egypt() =>
        Country.Create(new CountryData("Egypt", "مصر", "eg", "+20", "Egyptian Pound", "egp", "AR", "Africa/Cairo", 10, "🇪🇬", null));

    public static Country Kuwait() =>
        Country.Create(new CountryData("Kuwait", "الكويت", "KW", "+965", "Kuwaiti Dinar", "KWD", "ar", "Asia/Kuwait", 8, null, null));

    public static Governorate Cairo(Country egypt) => Governorate.Create(egypt, "Cairo", "القاهرة", "Nasr City", null);

    public static Plan Starter() =>
        Plan.Create("starter", new PlanTerms(new LocalizedText("Starter", "الباقة الأساسية"), 1500m, "egp", 1, 5, 500, 1));

    public static ClientProfile Profile(Country country, Governorate governorate) =>
        ClientProfile.Create("Al Noor Lab", "معمل النور", country, governorate.Id, "12 Tahrir St.", "01012345678", "LIC-1", "Dr. Mona", "+20 111 222 3333", "Info@AlNoor.example");

    public static Tenant Client(TenantSource source = TenantSource.Manual, int trialDays = 14)
    {
        var egypt = Egypt();
        var cairo = Cairo(egypt);
        return Tenant.Provision(Profile(egypt, cairo), egypt, cairo, Starter(), "al-noor", source, "en", trialDays, Now);
    }
}

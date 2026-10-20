namespace Platform.Domain.Authorization;

/// <summary>
/// The code-defined registry of every permission the platform knows (D-04). A policy attribute that names a key
/// outside this catalogue fails the build (RequestPolicyTests) and, as a last line, the authorization behavior
/// at runtime. Roles are composed from these keys by each tenant; the catalogue never names a role.
/// </summary>
/// <remarks>
/// The modules below are the platform modules the starter kit ships (01 §3.1). Business modules are added one
/// constant and one <see cref="PermissionModule"/> per bounded context as each intake (03 §11) is accepted.
/// </remarks>
public static class PermissionCatalog
{
    public const string Platform = "platform";
    public const string Identity = "identity";
    public const string Tenancy = "tenancy";
    public const string Organization = "organization";
    public const string Notifications = "notifications";
    public const string Audit = "audit";
    public const string Reporting = "reporting";
    public const string Files = "files";

    // Business modules (one per accepted intake; docs/build/MODULES.md maps SRS pages to modules)
    public const string Catalogue = "catalogue";
    public const string Patients = "patients";
    public const string Registration = "registration";
    public const string Samples = "samples";
    public const string Results = "results";
    public const string Billing = "billing";
    public const string Pricing = "pricing";
    public const string Claims = "claims";
    public const string Integration = "integration";
    public const string Automation = "automation";
    public const string Quality = "quality";
    public const string Settings = "settings";
    public const string Dashboard = "dashboard";
    public const string Support = "support";
    public const string Wallet = "wallet";

    private static readonly PermissionModule[] Modules =
    [
        new(Platform, "Platform administration", PermissionAction.View, PermissionAction.Manage),
        new(Identity, "Users and credentials", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Manage),
        new(Tenancy, "Tenants, plans and subscriptions", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Manage),
        // Export: the branches, roles and users registers as CSV (UAT R1-05: one export policy, as catalogue and pricing).
        new(Organization, "Branches, departments and roles", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Manage, PermissionAction.Export),
        new(Notifications, "Notifications and templates", PermissionAction.View, PermissionAction.Manage),
        new(Audit, "Audit trail and security events", PermissionAction.View, PermissionAction.Export),
        new(Reporting, "Reports and exports", PermissionAction.View, PermissionAction.Export),
        new(Files, "Attachments and documents", PermissionAction.View, PermissionAction.Create, PermissionAction.Manage),
        new(Catalogue, "Laboratory catalogue and lists", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Manage, PermissionAction.Export),
        new(Patients, "Patient files and documents", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Manage, PermissionAction.ViewConfidential, PermissionAction.Export),
        new(Registration, "Visits, orders and bookings", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Cancel, PermissionAction.Approve, PermissionAction.Manage, PermissionAction.Export),
        new(Samples, "Sample lifecycle", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Cancel, PermissionAction.Manage),
        new(Results, "Result entry, review, verification and printing", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Approve, PermissionAction.Cancel, PermissionAction.Manage, PermissionAction.Export),
        new(Billing, "Payments, refunds, transfers, cancellations and closing", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Approve, PermissionAction.Cancel, PermissionAction.Manage, PermissionAction.Export),
        new(Pricing, "Payers, contracts, price lists and contract pricing", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Approve, PermissionAction.Manage, PermissionAction.Export),
        new(Claims, "Payer claims", PermissionAction.View, PermissionAction.Create, PermissionAction.Approve, PermissionAction.Cancel, PermissionAction.Export),
        new(Integration, "Analysers, mappings, site agents and PCR kits", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Manage, PermissionAction.Export),
        new(Automation, "Auto-verification settings and medical rules", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Approve, PermissionAction.Manage),
        new(Quality, "Quality control", PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Manage),
        new(Settings, "Tenant parameters", PermissionAction.View, PermissionAction.Manage),
        new(Dashboard, "Dashboards and KPIs", PermissionAction.View),
        new(Support, "Support tickets", PermissionAction.View, PermissionAction.Create, PermissionAction.Manage),
        new(Wallet, "Wallet and subscription", PermissionAction.View, PermissionAction.Create, PermissionAction.Manage),
    ];

    private static readonly HashSet<string> KnownKeys = new(Modules.SelectMany(module => module.Keys), StringComparer.Ordinal);

    /// <summary>Every module, in display order.</summary>
    public static IReadOnlyList<PermissionModule> All => Modules;

    /// <summary>Every permission key, <c>module.action</c>.</summary>
    public static IReadOnlySet<string> Keys => KnownKeys;

    /// <summary>Builds the key for a module and an action: <c>module.action</c>.</summary>
    public static string Key(string module, PermissionAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        return $"{module}.{action.ToKey()}";
    }

    /// <summary>True when the key is catalogued. Comparison is exact: keys are case-sensitive.</summary>
    public static bool IsKnown(string permissionKey) => permissionKey is not null && KnownKeys.Contains(permissionKey);
}

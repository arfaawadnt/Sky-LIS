using Platform.Application.Abstractions.Security;
using Platform.Contracts.Patients;
using Platform.Contracts.Results;
using Platform.Domain.Results;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Results;

/// <summary>
/// VIP rule A-03 on an opened case and on the laboratory report (validation v0.5.0 F-06; assumption awaiting the business
/// owner, RES Q19): without the See-VIP special right the case view and the on-screen preview read "VIP patient" with no
/// Arabic name and no date of birth (the patient number, sex and age stay: the bench needs them), and the report of a VIP
/// patient does not print (RES-056). The registration desk and the patient file keep the identity (PAT Q8, REG Q12).
/// </summary>
internal static class ResultVip
{
    /// <summary>The name of a masked VIP patient (the clients show their own "VIP patient" text from the masked flag).</summary>
    public const string MaskedName = "VIP patient";

    public static bool IsMasked(PatientSummary? patient, IUserPrivileges privileges)
    {
        ArgumentNullException.ThrowIfNull(privileges);
        return patient is { IsVip: true } && !privileges.SpecialRights.CanSeeVip;
    }

    public static PatientHeaderView Mask(PatientHeaderView patient) =>
        patient with { FullName = MaskedName, NameAr = null, DateOfBirth = null, IsMasked = true };

    public static ReportPatientBlock Mask(ReportPatientBlock patient) =>
        patient with { FullName = MaskedName, NameAr = null, DateOfBirth = null, IsMasked = true };

    /// <summary>RES-056: the official copy of a VIP patient's report needs the See-VIP right.</summary>
    public static void EnsureCanPrint(PatientSummary? patient, IUserPrivileges privileges)
    {
        if (IsMasked(patient, privileges))
        {
            throw PrintRefusal();
        }
    }

    public static DomainException PrintRefusal() =>
        new(ResultsCodes.NoSeeVipToPrint, "Printing a VIP patient's report needs the See-VIP right.", DomainFault.Refused);
}

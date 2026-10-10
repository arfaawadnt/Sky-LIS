namespace Platform.Domain.Patients;

/// <summary>State table of a patient file (intake §6): Active is editable; Merged is frozen and terminal.</summary>
public enum PatientStatus
{
    Active = 0,
    Merged = 1,
}

/// <summary>The sex recorded on the file; reference ranges resolve by it (BR-08).</summary>
public enum PatientGender
{
    Male = 0,
    Female = 1,
}

/// <summary>Courtesy titles of the registration screen (LDM §4.2); a gendered title proposes the gender (<see cref="PatientTitles.DefaultGender"/>).</summary>
public enum PatientTitle
{
    Mr = 0,
    Mrs = 1,
    Miss = 2,
    Ms = 3,
    Dr = 4,
    Master = 5,
    Baby = 6,
    Prof = 7,
}

public static class PatientTitles
{
    /// <summary>The gender a title implies, or <see langword="null"/> for Dr, Prof and Baby. A default for the screen, not a rule (intake §16 Q2).</summary>
    public static PatientGender? DefaultGender(PatientTitle title) => title switch
    {
        PatientTitle.Mr or PatientTitle.Master => PatientGender.Male,
        PatientTitle.Mrs or PatientTitle.Miss or PatientTitle.Ms => PatientGender.Female,
        _ => null,
    };
}

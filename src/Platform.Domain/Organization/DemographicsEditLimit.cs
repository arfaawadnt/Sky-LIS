namespace Platform.Domain.Organization;

/// <summary>
/// How far a visit's results may have progressed while a member may still change the patient's identity demographics
/// (name, title, gender, date of birth or age, condition, national id, nationality) from Search registrations or the
/// patient file (change request 2026-10-13, item 1; LDM: patient data locks once results are signed unless a privilege
/// allows it). The values follow the result status model, lowest first: <see cref="Ordered"/> = only while no value has
/// been entered on the visit … <see cref="Printed"/> = always.
/// </summary>
public enum DemographicsEditLimit
{
    Ordered = 0,
    Complete = 1,
    Reviewed = 2,
    Verified = 3,
    Printed = 4,
}

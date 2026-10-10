namespace Platform.Domain.Catalogue;

/// <summary>
/// Which patients a service, a test or a reference-range band applies to (LDM §3.2 "Sex": blocks ordering for the
/// wrong sex; §3.3 ranges by sex). <see cref="Any"/> applies to everyone; as a patient's sex it means "not recorded".
/// </summary>
public enum GenderRestriction
{
    Any = 0,
    Male = 1,
    Female = 2,
}

using Platform.Application.Abstractions.Persistence;

namespace Platform.Application.Patients;

/// <summary>
/// The sortable columns of the patients register (CP-11, validation v0.5.0 B-03): the file number and the full name.
/// The repository maps them through <see cref="SortColumns{T}"/>; the validator refuses any other key.
/// </summary>
public static class PatientSortKeys
{
    public static readonly SortKeys Patients = new("number", "name");
}

using Platform.Domain.Patients;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Patients;

/// <summary>Scenarios S-T4 / S-T5 of intake LIS-INT-PAT-001: the content rules of INV-PAT-008 and the deactivation of INV-PAT-009.</summary>
public sealed class PatientDocumentTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly Guid Actor = Guid.CreateVersion7();
    private static readonly byte[] Pdf = "%PDF-1.7\n"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private const string Key = "0193a1b2c3d4e5f60718293a4b5c6d7e/patients/0193a1b2c3d4e5f60718293a4b5c6d7f/0193a1b2c3d4e5f60718293a4b5c6d80";
    private const string Hash = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";

    private static Patient ActivePatient(string number = "PAT-2026-00001") =>
        Patient.Register(number, new PatientDemographics(PatientTitle.Mr, "Omar Said", null, PatientGender.Male, new DateOnly(1980, 1, 1), null, null, null, null, null, null, null, null, null, null), PatientFieldRules.Optional, false, Now);

    [Theory]
    [InlineData("referral.pdf", "application/pdf", "pdf")]
    [InlineData("C:\\scans\\id-card.PNG", "image/png", "png")]
    [InlineData("../../x/photo.jpeg", "image/jpeg", "jpeg")]
    [InlineData("photo.JPG", "image/jpeg", "jpg")]
    public void S_T4_ok_Inspect_accepts_pdf_jpeg_and_png_by_extension_and_signature(string fileName, string contentType, string extension)
    {
        var header = contentType switch { "application/pdf" => Pdf, "image/png" => Png, _ => Jpeg };

        var file = PatientDocumentFiles.Inspect(fileName, 20_480, header);

        Assert.Equal((contentType, extension, 20_480L), (file.ContentType, file.Extension, file.SizeBytes));
        Assert.DoesNotContain('/', file.FileName);
        Assert.DoesNotContain('\\', file.FileName);
    }

    [Fact]
    public void S_T4_type_An_executable_is_refused_PAT_042_and_a_renamed_one_PAT_043()
    {
        AssertRefused(PatientCodes.DocumentTypeNotAllowed, () => PatientDocumentFiles.Inspect("setup.exe", 100, "MZ\x90\0"u8.ToArray()));
        AssertRefused(PatientCodes.DocumentTypeNotAllowed, () => PatientDocumentFiles.Inspect("no-extension", 100, Pdf));
        AssertRefused(PatientCodes.DocumentContentMismatch, () => PatientDocumentFiles.Inspect("invoice.pdf", 100, "MZ\x90\0"u8.ToArray()));
        AssertRefused(PatientCodes.DocumentContentMismatch, () => PatientDocumentFiles.Inspect("scan.png", 100, Jpeg));
    }

    [Fact]
    public void S_T4_size_Empty_and_oversized_files_are_refused_PAT_040_PAT_041()
    {
        AssertRefused(PatientCodes.DocumentEmpty, () => PatientDocumentFiles.Inspect("a.pdf", 0, []));
        AssertRefused(PatientCodes.DocumentTooLarge, () => PatientDocumentFiles.Inspect("a.pdf", PatientDocumentFiles.MaxSizeBytes + 1, Pdf));
        Assert.Equal(PatientDocumentFiles.MaxSizeBytes, PatientDocumentFiles.Inspect("a.pdf", PatientDocumentFiles.MaxSizeBytes, Pdf).SizeBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("folder/")]
    [InlineData("...")]
    public void S_T4_A_file_name_without_a_usable_name_is_refused_PAT_044(string fileName) =>
        AssertRefused(PatientCodes.DocumentFileNameInvalid, () => PatientDocumentFiles.Inspect(fileName, 100, Pdf));

    [Fact]
    public void S_T4_ok_Attach_records_the_document_with_its_detected_type_and_raises_PatientDocumentAttached()
    {
        var patient = ActivePatient();
        var file = PatientDocumentFiles.Inspect("referral.pdf", 2048, Pdf);

        var document = PatientDocument.Attach(patient, file, Key, Hash, "  from Dr. Adel  ", Actor, Now);

        Assert.Equal((patient.Id, "referral.pdf", "application/pdf", 2048L), (document.PatientId, document.FileName, document.ContentType, document.SizeBytes));
        Assert.Equal((Key, Hash.ToLowerInvariant(), "from Dr. Adel"), (document.StorageKey, document.Sha256, document.Note));
        Assert.Equal((Actor, Now, true), (document.UploadedByUserId, document.UploadedAtUtc, document.IsActive));
        var raised = Assert.IsType<PatientDocumentAttached>(Assert.Single(document.DomainEvents));
        Assert.Equal((document.Id, patient.Id, "application/pdf", 2048L), (raised.DocumentId, raised.PatientId, raised.ContentType, raised.SizeBytes));
    }

    [Fact]
    public void S_T4_merged_Attach_to_a_merged_file_is_refused_PAT_020()
    {
        var survivor = ActivePatient("PAT-2026-00001");
        var duplicate = ActivePatient("PAT-2026-00002");
        duplicate.MergeInto(survivor, Actor, "duplicate", false, Now);

        var refusal = Assert.Throws<InvalidStateTransitionException>(() => PatientDocument.Attach(duplicate, PatientDocumentFiles.Inspect("a.pdf", 10, Pdf), Key, Hash, null, Actor, Now));
        Assert.Equal(PatientCodes.PatientMerged, refusal.Code);
    }

    [Fact]
    public void S_T4_A_note_over_500_characters_is_refused_PAT_045() =>
        AssertRefused(PatientCodes.DocumentNoteTooLong, () => PatientDocument.Attach(ActivePatient(), PatientDocumentFiles.Inspect("a.pdf", 10, Pdf), Key, Hash, new string('n', 501), Actor, Now));

    [Fact]
    public void S_T5_ok_Deactivate_keeps_the_document_with_actor_time_and_reason()
    {
        var document = PatientDocument.Attach(ActivePatient(), PatientDocumentFiles.Inspect("a.pdf", 10, Pdf), Key, Hash, null, Actor, Now);
        document.ClearDomainEvents();
        var manager = Guid.CreateVersion7();

        document.Deactivate(manager, Now.AddHours(2), "  wrong patient  ");

        Assert.False(document.IsActive);
        Assert.Equal((manager, Now.AddHours(2), "wrong patient"), (document.DeactivatedByUserId!.Value, document.DeactivatedAtUtc!.Value, document.DeactivationReason));
        Assert.Equal(Key, document.StorageKey);
        var raised = Assert.IsType<PatientDocumentDeactivated>(Assert.Single(document.DomainEvents));
        Assert.Equal((manager, "wrong patient"), (raised.ActorUserId, raised.Reason));
    }

    [Fact]
    public void S_T5_Deactivate_needs_a_reason_PAT_047_and_happens_once_PAT_046()
    {
        var document = PatientDocument.Attach(ActivePatient(), PatientDocumentFiles.Inspect("a.pdf", 10, Pdf), Key, Hash, null, Actor, Now);

        AssertRefused(PatientCodes.DeactivationReasonRequired, () => document.Deactivate(Actor, Now, " "));
        Assert.True(document.IsActive);

        document.Deactivate(Actor, Now, "duplicate scan");
        var twice = Assert.Throws<InvalidStateTransitionException>(() => document.Deactivate(Actor, Now, "again"));
        Assert.Equal((PatientCodes.DocumentInactive, DomainFault.Conflict), (twice.Code, twice.Fault));
    }

    private static void AssertRefused(string code, Action action)
    {
        var refusal = Assert.ThrowsAny<DomainException>(action);
        Assert.Equal(code, refusal.Code);
    }
}

using Platform.Application.Catalogue.Commands;
using Platform.Contracts.Catalogue;
using Platform.Domain.Catalogue;

namespace Platform.Application.UnitTests.Catalogue;

/// <summary>Shape rules of the list-value command (T16) that answer a field error before the aggregate is read.</summary>
public sealed class LabTestValidatorTests
{
    [Fact]
    public void A_list_value_that_is_both_Normal_and_Critical_is_a_field_error_with_CAT_061()
    {
        var command = new ReplaceListValuesCommand(Guid.CreateVersion7(),
        [
            new ListValueItem("Yellow", true, false, false, false, null),
            new ListValueItem("Red", true, true, false, false, null),
        ]);

        var result = new ReplaceListValuesValidator().Validate(command);

        var failure = Assert.Single(result.Errors);
        Assert.Equal(("Values[1].IsCritical", LabTestCodes.ListValueNormalAndCritical), (failure.PropertyName, failure.ErrorCode));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Normal_or_Critical_alone_passes(bool isNormal, bool isCritical)
    {
        var command = new ReplaceListValuesCommand(Guid.CreateVersion7(), [new ListValueItem("Red", isNormal, isCritical, false, false, null)]);

        Assert.True(new ReplaceListValuesValidator().Validate(command).IsValid);
    }

    [Fact]
    public void Parent_services_are_distinct_non_empty_ids_and_may_be_none()
    {
        var renal = Guid.CreateVersion7();
        var validator = new SetLabTestParentServicesValidator();

        Assert.False(validator.Validate(new SetLabTestParentServicesCommand(Guid.CreateVersion7(), [renal, renal])).IsValid);
        Assert.False(validator.Validate(new SetLabTestParentServicesCommand(Guid.CreateVersion7(), [Guid.Empty])).IsValid);
        Assert.False(validator.Validate(new SetLabTestParentServicesCommand(Guid.Empty, [renal])).IsValid);
        Assert.True(validator.Validate(new SetLabTestParentServicesCommand(Guid.CreateVersion7(), [])).IsValid);
        Assert.True(validator.Validate(new SetLabTestParentServicesCommand(Guid.CreateVersion7(), [renal, Guid.CreateVersion7()])).IsValid);
    }
}

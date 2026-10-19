using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Billing;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of the payment methods set-up list (change request 2026-10-19, P-C10): tenant invisibility of
/// <c>app.payment_methods</c> (query filter and RLS), the code unique per tenant only, and the projections the handlers
/// rely on (options in sequence order, the other-active-cash check, the existing codes).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PaymentMethodModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task Payment_methods_of_another_tenant_are_invisible_and_their_codes_do_not_collide()
    {
        var tenant = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await DefineAsync(tenant, "CASH", PaymentMethod.Cash, sequence: 1);
        await DefineAsync(other, "CASH", PaymentMethod.Cash, sequence: 1);   // the same code in another tenant is fine

        using var scope = fixture.AppScope(other);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await context.Set<PaymentMethodDefinition>().IgnoreQueryFilters().CountAsync());   // RLS, not only the filter
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentMethodRepository>();
        Assert.Equal(["CASH"], await repository.ExistingCodesAsync(CancellationToken.None));
        Assert.Single((await repository.ListAsync(null, null, ListSort.Of(null, null), PageRequest.Normalized(1, 50), CancellationToken.None)).Items);
    }

    [DatabaseFact]
    public async Task The_code_is_unique_per_tenant_UNIQUE_409()
    {
        var tenant = Guid.CreateVersion7();
        await DefineAsync(tenant, "CARD", PaymentMethod.Card, sequence: 2);

        var refusal = await Assert.ThrowsAsync<DomainException>(() => DefineAsync(tenant, "card", PaymentMethod.Transfer, sequence: 3));

        Assert.Equal(ProblemCodes.UniqueViolation, refusal.Code);
    }

    [DatabaseFact]
    public async Task Options_follow_the_sequence_then_the_code_and_the_other_active_cash_check_sees_only_active_cash_methods()
    {
        var tenant = Guid.CreateVersion7();
        var petty = await DefineAsync(tenant, "PETTY", PaymentMethod.Cash, sequence: 1);
        var cash = await DefineAsync(tenant, "CASH", PaymentMethod.Cash, sequence: 1);
        await DefineAsync(tenant, "TRANSFER", PaymentMethod.Transfer, sequence: 0, requiresReference: true);
        var card = await DefineAsync(tenant, "CARD", PaymentMethod.Card, sequence: 2);

        using (var scope = fixture.AppScope(tenant))
        {
            var repository = scope.ServiceProvider.GetRequiredService<IPaymentMethodRepository>();
            var definition = await repository.GetForUpdateAsync(petty, CancellationToken.None);
            definition!.Deactivate(otherActiveCashExists: true, Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant);
        var methods = reader.ServiceProvider.GetRequiredService<IPaymentMethodRepository>();
        var options = await methods.OptionsAsync(CancellationToken.None);
        Assert.Equal(["TRANSFER", "CASH", "CARD"], options.Select(option => option.Code).ToArray());
        Assert.Equal([true, false, false], options.Select(option => option.RequiresReference).ToArray());
        Assert.False(await methods.OtherActiveCashExistsAsync(cash, CancellationToken.None));   // PETTY is inactive
        Assert.True(await methods.OtherActiveCashExistsAsync(card, CancellationToken.None));
        Assert.Null(await methods.FindActiveByCodeAsync("petty", CancellationToken.None));
        Assert.Equal("CASH", (await methods.FindActiveByCodeAsync(" cash ", CancellationToken.None))!.Code);
        Assert.True(await methods.CodeExistsAsync("PETTY", CancellationToken.None));
        var inactive = await methods.ListAsync(null, false, ListSort.Of("code", "asc"), PageRequest.Normalized(1, 50), CancellationToken.None);
        Assert.Equal("PETTY", Assert.Single(inactive.Items).Code);
    }

    private async Task<Guid> DefineAsync(Guid tenant, string code, PaymentMethod kind, int sequence, bool requiresReference = false)
    {
        using var scope = fixture.AppScope(tenant);
        var definition = PaymentMethodDefinition.Define(code, new LocalizedText(code, code), kind, requiresReference, sequence, Now);
        scope.ServiceProvider.GetRequiredService<IPaymentMethodRepository>().Add(definition);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return definition.Id;
    }
}

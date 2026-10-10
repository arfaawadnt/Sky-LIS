using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.Application.Abstractions.Security;

namespace Platform.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps the tenant and the elevation flag on every connection open (01 §4.5), which the
/// <c>tenant_isolation</c> policies read through <c>current_setting('app.current_tenant')</c>.
/// </summary>
/// <remarks>
/// The settings are session-scoped (<c>set_config(…, false)</c>), not <c>SET LOCAL</c>: a transaction-local
/// setting outside a transaction is a no-op with a warning, and EF Core does not wrap reads in transactions
/// (pitfall P-05). This is safe because Npgsql runs <c>DISCARD ALL</c> when a connection returns to the pool and
/// this interceptor stamps again on the next open; a connection string with <c>No Reset On Close</c> is refused
/// at registration. An unresolved tenant stamps the nil UUID, which matches no row (fail-closed). This
/// interceptor must be registered first (InterceptorOrderTests).
/// </remarks>
public sealed class TenantConnectionInterceptor(ITenantContextAccessor tenant) : DbConnectionInterceptor
{
    public const string StampSql = "SELECT set_config('app.current_tenant', @tenant, false), set_config('app.bypass_rls', @bypass, false)";

    private static readonly string NilTenant = Guid.Empty.ToString();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = BuildStampCommand(connection);
        command.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        var command = BuildStampCommand(connection);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand BuildStampCommand(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = connection.CreateCommand();
        command.CommandText = StampSql;
        AddParameter(command, "tenant", tenant.CurrentTenantId?.ToString() ?? NilTenant);
        AddParameter(command, "bypass", tenant.IsElevated ? "on" : "off");
        return command;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

using Platform.Application.Identity;
using Platform.Domain.Account;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Account;

/// <summary>The password policy of every self-service credential (02 §6.1 as decided on 2026-10-09): length only, never the username.</summary>
public static class PasswordRule
{
    public const int MaxLength = SessionPolicy.PasswordMaxLength;

    public static void Check(string password, string username, int minimumLength)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length < minimumLength || password.Length > MaxLength)
        {
            throw new DomainException(AccountCodes.PasswordLengthInvalid, $"The password needs {minimumLength} to {MaxLength} characters.", DomainFault.Validation);
        }

        var name = (username ?? string.Empty).Trim();
        if (name.Length > 0 && password.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(AccountCodes.PasswordContainsUsername, "The password must not contain the username.", DomainFault.Validation);
        }
    }
}

namespace Platform.Contracts.Common;

/// <summary>
/// Marks a request property that carries a credential (D-03). The browser seals every such property into a
/// compact JWE before sending; the WebApi edge filter opens it before the action runs. A plaintext value where
/// an envelope is required is refused with <c>CRED-005</c>. Gate: CredentialFieldTests (every password-named
/// string property on a request record carries this attribute).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CredentialFieldAttribute : Attribute;

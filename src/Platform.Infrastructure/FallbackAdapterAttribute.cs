namespace Platform.Infrastructure;

/// <summary>
/// Marks a convention-registered adapter as a placeholder for a port whose real adapter arrives with a later module
/// (for example "no result is verified yet" until the Results module exists). The convention scan registers a
/// fallback only when no ordinary adapter implements the same port, so the real adapter replaces it without any
/// registration change. Remove the fallback class once the real adapter has shipped.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FallbackAdapterAttribute : Attribute;

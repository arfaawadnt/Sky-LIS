using System.Reflection;
using Platform.Application;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Infrastructure;
using Platform.SharedKernel.Primitives;

namespace Platform.ArchitectureTests.Support;

/// <summary>The six production assemblies and the repository they live in. One marker type per assembly keeps the references explicit.</summary>
internal static class SolutionAssemblies
{
    public const string SharedKernel = "Platform.SharedKernel";
    public const string Domain = "Platform.Domain";
    public const string Contracts = "Platform.Contracts";
    public const string Application = "Platform.Application";
    public const string Infrastructure = "Platform.Infrastructure";
    public const string WebApi = "Platform.WebApi";

    public static readonly Assembly SharedKernelAssembly = typeof(Entity).Assembly;
    public static readonly Assembly DomainAssembly = typeof(PermissionCatalog).Assembly;
    public static readonly Assembly ContractsAssembly = typeof(ProblemCodes).Assembly;
    public static readonly Assembly ApplicationAssembly = typeof(ApplicationServiceRegistration).Assembly;
    public static readonly Assembly InfrastructureAssembly = typeof(InfrastructureServiceRegistration).Assembly;
    public static readonly Assembly WebApiAssembly = typeof(Program).Assembly;

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>True for assemblies that ship with the runtime (the BCL).</summary>
    public static bool IsBcl(string assemblyName) =>
        assemblyName is "System" or "netstandard" or "mscorlib"
        || assemblyName.StartsWith("System.", StringComparison.Ordinal);

    /// <summary>The simple names of the assemblies an assembly's IL references (compile-time dependencies that are actually used).</summary>
    public static IReadOnlyList<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).OrderBy(name => name, StringComparer.Ordinal).ToList();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Platform.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Platform.sln was not found above {AppContext.BaseDirectory}.");
    }
}

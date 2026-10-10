using NetArchTest.Rules;
using Platform.ArchitectureTests.Support;

namespace Platform.ArchitectureTests;

/// <summary>
/// The reference matrix of 01 §2.1, enforced twice: on the IL (what an assembly actually uses, via NetArchTest
/// and the referenced-assembly list) and on the project file (what it declares, forbidden even when unused).
/// </summary>
public sealed class LayerRulesTests
{
    private static readonly string[] PersistenceAndHostNamespaces =
        ["Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "Microsoft.Data"];

    private static readonly string[] ForbiddenForDomain =
        ["Platform.Application", "Platform.Infrastructure", "Platform.Contracts", "Platform.WebApi", "MediatR", "FluentValidation", "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "Microsoft.Extensions"];

    private static readonly string[] ForbiddenForContracts =
        ["Platform.Domain", "Platform.Application", "Platform.Infrastructure", "Platform.WebApi", "MediatR", "FluentValidation", "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore"];

    private static readonly string[] ForbiddenForApplication =
        ["Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "Microsoft.Data", "Platform.Infrastructure", "Platform.WebApi"];

    private static readonly string[] ForbiddenForInfrastructure =
        ["Microsoft.AspNetCore.Mvc", "Platform.WebApi"];

    private static readonly string[] ForbiddenForSharedKernel =
        ["Platform.Domain", "Platform.Contracts", "Platform.Application", "Platform.Infrastructure", "Platform.WebApi"];

    private static readonly string[] SharedKernelOnly = [SolutionAssemblies.SharedKernel];
    private static readonly string[] ApplicationProjectReferences = [SolutionAssemblies.Contracts, SolutionAssemblies.Domain, SolutionAssemblies.SharedKernel];
    private static readonly string[] InfrastructureProjectReferences = [SolutionAssemblies.Application, SolutionAssemblies.Domain, SolutionAssemblies.SharedKernel];
    private static readonly string[] WebApiProjectReferences = [SolutionAssemblies.Application, SolutionAssemblies.Contracts, SolutionAssemblies.Infrastructure];

    private static readonly string[] AllowedApplicationPackages =
        ["FluentValidation", "FluentValidation.DependencyInjectionExtensions", "MediatR", "Microsoft.Extensions.DependencyInjection.Abstractions"];

    private static readonly string[] AllowedInfrastructurePackages =
        ["EFCore.NamingConventions", "Konscious.Security.Cryptography.Argon2", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Design", "Microsoft.EntityFrameworkCore.Relational", "Microsoft.Extensions.Caching.Memory", "Microsoft.IdentityModel.JsonWebTokens", "Microsoft.IdentityModel.Tokens", "Npgsql", "Npgsql.EntityFrameworkCore.PostgreSQL"];

    [Fact]
    public void SharedKernel_references_the_BCL_only()
    {
        var project = ProjectFile.Load(SolutionAssemblies.SharedKernel);
        Assert.Empty(project.PackageReferences);
        Assert.Empty(project.ProjectReferences);

        var nonBcl = SolutionAssemblies.ReferencedNames(SolutionAssemblies.SharedKernelAssembly).Where(name => !SolutionAssemblies.IsBcl(name)).ToList();
        Assert.True(nonBcl.Count == 0, "SharedKernel references non-BCL assemblies: " + string.Join(", ", nonBcl));

        AssertNoDependency(SolutionAssemblies.SharedKernelAssembly, ForbiddenForSharedKernel);
    }

    [Fact]
    public void Domain_references_SharedKernel_only()
    {
        var project = ProjectFile.Load(SolutionAssemblies.Domain);
        Assert.Empty(project.PackageReferences);
        Assert.Equal(SharedKernelOnly, project.ProjectReferences);

        var unexpected = SolutionAssemblies.ReferencedNames(SolutionAssemblies.DomainAssembly)
            .Where(name => !SolutionAssemblies.IsBcl(name) && name != SolutionAssemblies.SharedKernel)
            .ToList();
        Assert.True(unexpected.Count == 0, "Domain references assemblies outside SharedKernel and the BCL: " + string.Join(", ", unexpected));

        AssertNoDependency(SolutionAssemblies.DomainAssembly, ForbiddenForDomain);
    }

    [Fact]
    public void Contracts_references_SharedKernel_only()
    {
        var project = ProjectFile.Load(SolutionAssemblies.Contracts);
        Assert.Empty(project.PackageReferences);
        Assert.Equal(SharedKernelOnly, project.ProjectReferences);

        var unexpected = SolutionAssemblies.ReferencedNames(SolutionAssemblies.ContractsAssembly)
            .Where(name => !SolutionAssemblies.IsBcl(name) && name != SolutionAssemblies.SharedKernel)
            .ToList();
        Assert.True(unexpected.Count == 0, "Contracts references assemblies outside SharedKernel and the BCL: " + string.Join(", ", unexpected));

        AssertNoDependency(SolutionAssemblies.ContractsAssembly, ForbiddenForContracts);
    }

    [Fact]
    public void Application_carries_no_EntityFrameworkCore()
    {
        // IL: nothing in Application uses EF Core, Npgsql, ASP.NET or the outer layers (pitfall P-01).
        AssertNoDependency(SolutionAssemblies.ApplicationAssembly, ForbiddenForApplication);

        var leaked = SolutionAssemblies.ReferencedNames(SolutionAssemblies.ApplicationAssembly)
            .Where(name => PersistenceAndHostNamespaces.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
        Assert.True(leaked.Count == 0, "Application references persistence or host assemblies: " + string.Join(", ", leaked));

        // Project file: only the allowed packages and the three inner projects, even if unused.
        var project = ProjectFile.Load(SolutionAssemblies.Application);
        var forbiddenPackages = project.PackageReferences.Except(AllowedApplicationPackages, StringComparer.Ordinal).ToList();
        Assert.True(forbiddenPackages.Count == 0, "Application declares packages outside the allowed set: " + string.Join(", ", forbiddenPackages));
        Assert.Equal(ApplicationProjectReferences, project.ProjectReferences);
    }

    [Fact]
    public void Infrastructure_references_Application_Domain_SharedKernel_and_persistence_packages_only()
    {
        var project = ProjectFile.Load(SolutionAssemblies.Infrastructure);
        Assert.Equal(InfrastructureProjectReferences, project.ProjectReferences);

        var forbiddenPackages = project.PackageReferences.Except(AllowedInfrastructurePackages, StringComparer.Ordinal).ToList();
        Assert.True(forbiddenPackages.Count == 0, "Infrastructure declares packages outside the allowed set: " + string.Join(", ", forbiddenPackages));

        AssertNoDependency(SolutionAssemblies.InfrastructureAssembly, ForbiddenForInfrastructure);
    }

    [Fact]
    public void WebApi_references_Application_Infrastructure_and_Contracts()
    {
        var project = ProjectFile.Load(SolutionAssemblies.WebApi);
        Assert.Equal(WebApiProjectReferences, project.ProjectReferences);
    }

    [Fact]
    public void Every_project_reference_stays_inside_this_repository()
    {
        // Workspace isolation (CLAUDE.md §1): LIS never references, imports or borrows another application on this machine.
        var root = SolutionAssemblies.RepositoryRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var offenders = ProjectFile.All()
            .SelectMany(project => project.ProjectReferencePaths.Select(path => (project.Name, path)))
            .Where(entry => !entry.path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                            || entry.path.Contains("QAMS", StringComparison.OrdinalIgnoreCase)
                            || entry.path.Contains("QMS", StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{entry.Name} -> {entry.path}")
            .ToList();

        Assert.True(offenders.Count == 0, "Project references leave the repository: " + string.Join(", ", offenders));
    }

    private static void AssertNoDependency(System.Reflection.Assembly assembly, string[] forbiddenNamespaces)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbiddenNamespaces).GetResult();
        var failing = result.FailingTypeNames ?? [];
        Assert.True(result.IsSuccessful, $"{assembly.GetName().Name} depends on a forbidden namespace through: " + string.Join(", ", failing));
    }
}

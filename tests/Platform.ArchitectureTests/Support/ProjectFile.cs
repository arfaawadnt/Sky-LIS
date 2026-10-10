using System.Xml.Linq;

namespace Platform.ArchitectureTests.Support;

/// <summary>
/// A parsed .csproj: the package and project references it declares. The IL-level checks see only dependencies
/// that are used; this sees every dependency that is declared, so an unused but forbidden package still fails.
/// </summary>
internal sealed class ProjectFile
{
    private ProjectFile(string name, string path, IReadOnlyList<string> packageReferences, IReadOnlyList<string> projectReferencePaths)
    {
        Name = name;
        Path = path;
        PackageReferences = packageReferences;
        ProjectReferencePaths = projectReferencePaths;
        ProjectReferences = projectReferencePaths.Select(referencePath => System.IO.Path.GetFileNameWithoutExtension(referencePath)).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    public string Name { get; }

    public string Path { get; }

    /// <summary>Package ids, sorted.</summary>
    public IReadOnlyList<string> PackageReferences { get; }

    /// <summary>Referenced project names (file name without extension), sorted.</summary>
    public IReadOnlyList<string> ProjectReferences { get; }

    /// <summary>Referenced project files as absolute paths.</summary>
    public IReadOnlyList<string> ProjectReferencePaths { get; }

    public static ProjectFile Load(string projectName)
    {
        foreach (var folder in new[] { "src", "tests" })
        {
            var candidate = System.IO.Path.Combine(SolutionAssemblies.RepositoryRoot, folder, projectName, projectName + ".csproj");
            if (File.Exists(candidate))
            {
                return Parse(candidate);
            }
        }

        throw new FileNotFoundException($"Project file for '{projectName}' was not found under src/ or tests/.");
    }

    /// <summary>Every project under src/ and tests/.</summary>
    public static IReadOnlyList<ProjectFile> All()
    {
        var root = SolutionAssemblies.RepositoryRoot;
        return new[] { "src", "tests" }
            .Select(folder => System.IO.Path.Combine(root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories))
            .Select(Parse)
            .ToList();
    }

    private static ProjectFile Parse(string path)
    {
        var document = XDocument.Load(path);
        var directory = System.IO.Path.GetDirectoryName(path)!;

        var packages = document.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .OrderBy(include => include, StringComparer.Ordinal)
            .ToList();

        var projects = document.Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, include!)))
            .ToList();

        return new ProjectFile(System.IO.Path.GetFileNameWithoutExtension(path), path, packages, projects);
    }
}

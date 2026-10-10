using Platform.ArchitectureTests.Support;

namespace Platform.ArchitectureTests;

/// <summary>
/// TransitionAtomicityTests (01 §8, pitfall P-02): a business command handler commits exactly once, so the state
/// change, the recorded metadata and the events land in one transaction. Identity handlers are the documented
/// exception: they commit on mutually exclusive branches (a failed check is persisted before the refusal), and the
/// recovery-start handler changes nothing (the one-time code service persists its own facts).
/// </summary>
public sealed class TransitionAtomicityTests
{
    private static readonly string[] IdentityHandlers = ["SignIn.cs", "RefreshSessionCommand.cs", "PasswordRecovery.cs"];

    [Fact]
    public void Every_business_command_handler_calls_SaveChangesAsync_exactly_once()
    {
        var commands = Directory.EnumerateFiles(Path.Combine(SolutionAssemblies.RepositoryRoot, "src", "Platform.Application"), "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Commands{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !IdentityHandlers.Contains(Path.GetFileName(path)))
            .ToList();
        Assert.NotEmpty(commands);

        foreach (var path in commands)
        {
            var source = File.ReadAllText(path);
            var handlers = CountOccurrences(source, " : ICommandHandler<");
            var commits = CountOccurrences(source, "SaveChangesAsync(");
            Assert.True(handlers > 0, $"{Path.GetFileName(path)} declares no handler");
            Assert.True(handlers == commits, $"{Path.GetFileName(path)}: {handlers} handler(s) but {commits} SaveChangesAsync call(s); one commit per command");
        }
    }

    [Fact]
    public void Query_handlers_never_commit()
    {
        var queries = Directory.EnumerateFiles(Path.Combine(SolutionAssemblies.RepositoryRoot, "src", "Platform.Application"), "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Queries{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(queries);

        foreach (var path in queries)
        {
            Assert.DoesNotContain("SaveChangesAsync", File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        for (var index = text.IndexOf(token, StringComparison.Ordinal); index >= 0; index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}

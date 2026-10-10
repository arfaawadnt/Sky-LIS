using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Platform.Application.Abstractions.Files;
using Platform.Application.Abstractions.Security;

namespace Platform.Infrastructure.Services.Files;

/// <summary>
/// <see cref="IFileStore"/> on the local file system under <c>Lis:Files:Root</c> (default <c>E:\LIS\.local\files</c>;
/// production <c>E:\lis-data\files</c>), created on demand. Layout <c>&lt;root&gt;/&lt;tenant&gt;/&lt;area&gt;/&lt;owner&gt;/&lt;id&gt;</c>
/// with every segment generated here (hex GUIDs and a lower-case area word), so no client text reaches a path. Writes go
/// to a temporary file that is renamed when complete; the SHA-256 is computed while writing. Reads and deletes accept
/// only well-formed keys of the current tenant that resolve inside the root.
/// </summary>
internal sealed partial class LocalFileStore(ICurrentTenant tenant, IConfiguration? configuration = null) : IFileStore
{
    public const string RootKey = "Lis:Files:Root";
    public const string DefaultRoot = @"E:\LIS\.local\files";
    private const int BufferSize = 81920;

    public async Task<StoredFile> SaveAsync(string area, Guid ownerId, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        if (!AreaPattern().IsMatch(area ?? string.Empty))
        {
            throw new ArgumentException("The area must be a lower-case word of 1 to 32 characters.", nameof(area));
        }

        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Files are stored for a resolved tenant only.");
        var key = $"{tenantId:N}/{area}/{ownerId:N}/{Guid.CreateVersion7():N}";
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporary = path + ".partial";
        long total = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new IOException($"The content exceeds {maxBytes} bytes.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temporary, path);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }

        return new StoredFile(key, total, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = OwnPath(storageKey);
        if (path is null || !File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = OwnPath(storageKey);
        if (path is not null)
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>The configured root, absolute.</summary>
    public string Root
    {
        get
        {
            var configured = configuration?[RootKey];
            return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? DefaultRoot : configured);
        }
    }

    /// <summary>The path of a key of the current tenant, or <see langword="null"/> for a malformed key, another tenant's key or a path outside the root.</summary>
    private string? OwnPath(string? storageKey)
    {
        if (storageKey is null || tenant.TenantId is not { } tenantId)
        {
            return null;
        }

        var match = KeyPattern().Match(storageKey);
        if (!match.Success || !string.Equals(match.Groups["tenant"].Value, tenantId.ToString("N"), StringComparison.Ordinal))
        {
            return null;
        }

        return PathOf(storageKey);
    }

    private string PathOf(string key)
    {
        var root = Root;
        var full = Path.GetFullPath(Path.Combine([root, .. key.Split('/')]));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A storage key resolved outside the file root.");
        }

        return full;
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex AreaPattern();

    [GeneratedRegex("^(?<tenant>[0-9a-f]{32})/[a-z][a-z0-9-]{0,31}/[0-9a-f]{32}/[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}

using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Platform.Application.Abstractions.Security;
using Platform.Infrastructure.Services.Files;

namespace Platform.Application.UnitTests.Patients;

/// <summary>The shared file store (INV-PAT-008): server-generated keys per tenant, bounded writes, tenant-fenced reads.</summary>
public sealed class LocalFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lis-file-store-tests", Guid.CreateVersion7().ToString("N"));
    private readonly FakeTenant _tenant = new() { TenantId = Guid.CreateVersion7() };

    private LocalFileStore Store => new(_tenant, new RootConfiguration(_root));

    [Fact]
    public async Task Save_writes_under_a_generated_key_of_the_current_tenant_and_reads_back()
    {
        var owner = Guid.CreateVersion7();
        var bytes = "%PDF-1.7 hello"u8.ToArray();

        var stored = await Store.SaveAsync("patients", owner, new MemoryStream(bytes), 1024, CancellationToken.None);

        Assert.Matches($"^{_tenant.TenantId:N}/patients/{owner:N}/[0-9a-f]{{32}}$", stored.StorageKey);
        Assert.Equal(bytes.Length, stored.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), stored.Sha256);
        Assert.True(File.Exists(Path.Combine([_root, .. stored.StorageKey.Split('/')])));

        await using var read = await Store.OpenReadAsync(stored.StorageKey, CancellationToken.None);
        Assert.NotNull(read);
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task Another_tenant_malformed_or_traversing_keys_read_nothing()
    {
        var stored = await Store.SaveAsync("patients", Guid.CreateVersion7(), new MemoryStream([1, 2, 3]), 1024, CancellationToken.None);
        var other = new LocalFileStore(new FakeTenant { TenantId = Guid.CreateVersion7() }, new RootConfiguration(_root));

        Assert.Null(await other.OpenReadAsync(stored.StorageKey, CancellationToken.None));
        Assert.Null(await Store.OpenReadAsync("../../windows/win.ini", CancellationToken.None));
        Assert.Null(await Store.OpenReadAsync($"{_tenant.TenantId:N}/patients/../../x", CancellationToken.None));
        Assert.Null(await Store.OpenReadAsync($"{_tenant.TenantId:N}/patients/{Guid.CreateVersion7():N}/{Guid.CreateVersion7():N}", CancellationToken.None));

        await other.DeleteAsync(stored.StorageKey, CancellationToken.None);   // another tenant cannot remove it either
        await using var stillThere = await Store.OpenReadAsync(stored.StorageKey, CancellationToken.None);
        Assert.NotNull(stillThere);
    }

    [Fact]
    public async Task Oversized_content_is_refused_and_leaves_nothing_behind()
    {
        var owner = Guid.CreateVersion7();

        await Assert.ThrowsAsync<IOException>(() => Store.SaveAsync("patients", owner, new MemoryStream(new byte[2049]), 2048, CancellationToken.None));

        var folder = Path.Combine(_root, _tenant.TenantId!.Value.ToString("N"), "patients", owner.ToString("N"));
        Assert.Empty(Directory.Exists(folder) ? Directory.GetFiles(folder) : []);
    }

    [Fact]
    public async Task Saving_needs_a_tenant_and_a_well_formed_area()
    {
        var anonymous = new LocalFileStore(new FakeTenant(), new RootConfiguration(_root));

        await Assert.ThrowsAsync<InvalidOperationException>(() => anonymous.SaveAsync("patients", Guid.CreateVersion7(), new MemoryStream([1]), 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.SaveAsync("../etc", Guid.CreateVersion7(), new MemoryStream([1]), 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.SaveAsync("Patients", Guid.CreateVersion7(), new MemoryStream([1]), 10, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_removes_the_content_of_an_own_key()
    {
        var stored = await Store.SaveAsync("patients", Guid.CreateVersion7(), new MemoryStream([1, 2]), 10, CancellationToken.None);

        await Store.DeleteAsync(stored.StorageKey, CancellationToken.None);

        Assert.Null(await Store.OpenReadAsync(stored.StorageKey, CancellationToken.None));
        await Store.DeleteAsync(stored.StorageKey, CancellationToken.None);   // idempotent
    }

    [Fact]
    public async Task Copy_reads_another_tenants_key_only_when_elevated_and_writes_a_new_key_of_the_current_tenant()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var stored = await Store.SaveAsync("catalogue", Guid.CreateVersion7(), new MemoryStream(bytes), 1024, CancellationToken.None);
        var targetTenant = Guid.CreateVersion7();
        var owner = Guid.CreateVersion7();

        var plain = new LocalFileStore(new FakeTenant { TenantId = targetTenant }, new RootConfiguration(_root));
        await Assert.ThrowsAsync<InvalidOperationException>(() => plain.CopyAsync(stored.StorageKey, "catalogue", owner, 1024, CancellationToken.None));

        var elevated = new LocalFileStore(new FakeTenant { TenantId = targetTenant, IsElevated = true }, new RootConfiguration(_root));
        var copy = await elevated.CopyAsync(stored.StorageKey, "catalogue", owner, 1024, CancellationToken.None);
        Assert.NotNull(copy);
        Assert.Matches($"^{targetTenant:N}/catalogue/{owner:N}/{copy.FileId:N}$", copy.StorageKey);
        Assert.NotEqual(stored.FileId, copy.FileId);
        Assert.Equal(stored.Sha256, copy.Sha256);
        await using (var read = await plain.OpenReadAsync(copy.StorageKey, CancellationToken.None))
        {
            Assert.NotNull(read);   // the copy belongs to the target tenant
        }

        Assert.Null(await elevated.CopyAsync($"{_tenant.TenantId:N}/catalogue/{owner:N}/{Guid.CreateVersion7():N}", "catalogue", owner, 1024, CancellationToken.None));   // missing content
        Assert.Null(await elevated.CopyAsync("../../windows/win.ini", "catalogue", owner, 1024, CancellationToken.None));   // malformed key
        await Assert.ThrowsAsync<IOException>(() => elevated.CopyAsync(stored.StorageKey, "catalogue", owner, 4, CancellationToken.None));
    }

    [Fact]
    public void The_root_defaults_to_the_development_folder_and_follows_configuration()
    {
        Assert.Equal(Path.GetFullPath(LocalFileStore.DefaultRoot), new LocalFileStore(_tenant).Root);
        Assert.Equal(Path.GetFullPath(_root), Store.Root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeTenant : ICurrentTenant
    {
        public Guid? TenantId { get; init; }

        public bool IsElevated { get; init; }
    }

    /// <summary>The one key the store reads; nothing else of IConfiguration is used.</summary>
    private sealed class RootConfiguration(string root) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == LocalFileStore.RootKey ? root : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}

using Microsoft.Extensions.Options;

namespace PartyPix.Web.Services;

public class StorageOptions
{
    public string RootPath { get; set; } = "media-store";
}

public class LocalDiskStorage : IStorageService
{
    private readonly string _root;

    public LocalDiskStorage(IOptions<StorageOptions> opts, IHostEnvironment env)
    {
        var path = opts.Value.RootPath;
        _root = Path.IsPathRooted(path) ? path : Path.Combine(env.ContentRootPath, path);
        Directory.CreateDirectory(_root);
    }

    public string ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ArgumentException("storageKey required", nameof(storageKey));

        // Guard against traversal. Normalize separators first.
        var normalized = storageKey.Replace('\\', '/').TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(_root, normalized));
        var rootFull = Path.GetFullPath(_root);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escapes storage root.");

        return full;
    }

    public async Task<string> SaveAsync(string storageKey, Stream content, CancellationToken ct = default)
    {
        var full = ResolvePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        await content.CopyToAsync(fs, ct);
        return storageKey;
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var full = ResolvePath(storageKey);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    public bool Exists(string storageKey) => File.Exists(ResolvePath(storageKey));

    public Stream OpenRead(string storageKey) =>
        File.OpenRead(ResolvePath(storageKey));
}

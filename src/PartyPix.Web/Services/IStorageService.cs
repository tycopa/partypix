namespace PartyPix.Web.Services;

public interface IStorageService
{
    /// <summary>Absolute path on disk for the given storage key.</summary>
    string ResolvePath(string storageKey);

    Task<string> SaveAsync(string storageKey, Stream content, CancellationToken ct = default);
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
    bool Exists(string storageKey);
    Stream OpenRead(string storageKey);
}

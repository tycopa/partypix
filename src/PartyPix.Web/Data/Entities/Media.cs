namespace PartyPix.Web.Data.Entities;

public enum MediaKind
{
    Image = 0,
    Video = 1,
}

public enum MediaStatus
{
    Pending = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
    Hidden = 4,
}

public class Media
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EventId { get; set; }
    public Event? Event { get; set; }

    public Guid? AlbumId { get; set; }
    public Album? Album { get; set; }

    public Guid? GuestSessionId { get; set; }
    public GuestSession? GuestSession { get; set; }

    public MediaKind Kind { get; set; }
    public MediaStatus Status { get; set; } = MediaStatus.Pending;

    public string OriginalFileName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }

    /// <summary>Relative path under the storage root, e.g. "events/{slug}/orig/{id}.jpg".</summary>
    public string StorageKey { get; set; } = default!;
    public string? ThumbnailKey { get; set; }
    public string? DisplayKey { get; set; } // web-sized version for gallery
    public string? PosterKey { get; set; }  // video poster frame

    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }

    /// <summary>
    /// SHA-256 hex of the original bytes. Set when the upload pipeline
    /// finishes hashing; used to reject duplicate re-uploads of the same
    /// file (per event). Nullable because rows that predate this column
    /// don't have one.
    /// </summary>
    public string? ContentHash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TakenAt { get; set; }
}

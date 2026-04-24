namespace PartyPix.Web.Data.Entities;

public class Event
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string HostUserId { get; set; } = default!;
    public AppUser? Host { get; set; }

    /// <summary>URL-safe identifier used in /e/{slug}.</summary>
    public string Slug { get; set; } = default!;

    public string Title { get; set; } = default!;
    public string? WelcomeMessage { get; set; }
    public DateTime EventDate { get; set; }

    public string Theme { get; set; } = "default";

    /// <summary>Optional access code required on the welcome page.</summary>
    public string? AccessCode { get; set; }

    public bool GuestUploadsEnabled { get; set; } = true;
    public bool GuestbookEnabled { get; set; } = true;
    public bool LikesEnabled { get; set; } = true;
    public bool CommentsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When uploads close. Null = open indefinitely.</summary>
    public DateTime? UploadsCloseAt { get; set; }

    public ICollection<Album> Albums { get; set; } = new List<Album>();
    public ICollection<Media> Media { get; set; } = new List<Media>();
    public ICollection<GuestSession> GuestSessions { get; set; } = new List<GuestSession>();
    public ICollection<GuestbookEntry> GuestbookEntries { get; set; } = new List<GuestbookEntry>();
}

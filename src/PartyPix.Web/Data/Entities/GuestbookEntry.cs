namespace PartyPix.Web.Data.Entities;

public enum GuestbookKind
{
    Text = 0,
    Video = 1,
}

public class GuestbookEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EventId { get; set; }
    public Event? Event { get; set; }

    public Guid? GuestSessionId { get; set; }
    public GuestSession? GuestSession { get; set; }

    public GuestbookKind Kind { get; set; }
    public string? Message { get; set; }
    public Guid? MediaId { get; set; } // for video guestbook

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Hidden { get; set; }
}

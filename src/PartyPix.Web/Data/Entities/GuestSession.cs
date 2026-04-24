namespace PartyPix.Web.Data.Entities;

public class GuestSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>Opaque token stored in a cookie on the guest's device.</summary>
    public string CookieToken { get; set; } = default!;

    public string DisplayName { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public string? UserAgent { get; set; }
    public string? IpHash { get; set; }
}

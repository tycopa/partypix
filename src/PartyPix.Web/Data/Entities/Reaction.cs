namespace PartyPix.Web.Data.Entities;

public enum ReactionKind
{
    Like = 0,
    Comment = 1,
}

public class Reaction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MediaId { get; set; }
    public Media? Media { get; set; }

    public Guid GuestSessionId { get; set; }
    public GuestSession? GuestSession { get; set; }

    public ReactionKind Kind { get; set; }
    public string? Body { get; set; } // null for likes

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

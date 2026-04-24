namespace PartyPix.Web.Data.Entities;

public class Album
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EventId { get; set; }
    public Event? Event { get; set; }

    public string Name { get; set; } = default!;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Media> Media { get; set; } = new List<Media>();
}

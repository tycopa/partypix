using Microsoft.AspNetCore.Identity;

namespace PartyPix.Web.Data.Entities;

public class AppUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Event> Events { get; set; } = new List<Event>();
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using PartyPix.Web.Services;

namespace PartyPix.Web.Pages.E;

public class ContributorsModel(AppDbContext db, GuestSessionAccessor guests) : PageModel
{
    public Event Event { get; private set; } = default!;
    public GuestSession Guest { get; private set; } = default!;
    public List<Contributor> Contributors { get; private set; } = new();
    public int TotalCount { get; private set; }

    /// <summary>
    /// Per-guest aggregate. SampleMediaId points at the contributor's most
    /// recent Ready upload so the card can show a representative thumbnail.
    /// </summary>
    public record Contributor(Guid Id, string DisplayName, int Count, Guid? SampleMediaId);

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Slug == slug, ct);
        if (ev is null) return NotFound();

        var guest = await guests.GetAsync(ev, ct);
        if (guest is null) return RedirectToPage("/E/Welcome", new { slug });

        Event = ev;
        Guest = guest;

        Contributors = await db.GuestSessions
            .Where(g => g.EventId == ev.Id)
            .Select(g => new Contributor(
                g.Id,
                g.DisplayName,
                db.Media.Count(m => m.GuestSessionId == g.Id
                                    && m.EventId == ev.Id
                                    && m.Status == MediaStatus.Ready),
                db.Media
                    .Where(m => m.GuestSessionId == g.Id
                                && m.EventId == ev.Id
                                && m.Status == MediaStatus.Ready)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => (Guid?)m.Id)
                    .FirstOrDefault()))
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.DisplayName)
            .ToListAsync(ct);

        TotalCount = Contributors.Sum(c => c.Count);
        return Page();
    }
}

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

        // Step 1: aggregate Ready media by guest session in a single SQL
        // query. Group-by + Count + most-recent-id translates cleanly without
        // post-projection Where filters that EF sometimes refuses.
        var aggregates = await db.Media
            .Where(m => m.EventId == ev.Id
                        && m.Status == MediaStatus.Ready
                        && m.GuestSessionId != null)
            .GroupBy(m => m.GuestSessionId!.Value)
            .Select(g => new
            {
                GuestId = g.Key,
                Count = g.Count(),
                SampleId = g.OrderByDescending(m => m.CreatedAt)
                            .Select(m => (Guid?)m.Id)
                            .FirstOrDefault(),
            })
            .ToListAsync(ct);

        if (aggregates.Count == 0) return Page();

        // Step 2: pull the display names for those sessions in one round trip
        // and merge in memory. The list is bounded by the number of guests
        // who actually uploaded, so this stays small.
        var ids = aggregates.Select(a => a.GuestId).ToList();
        var names = await db.GuestSessions
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.DisplayName })
            .ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);

        Contributors = aggregates
            .Select(a => new Contributor(
                a.GuestId,
                names.TryGetValue(a.GuestId, out var n) ? n : "(unknown)",
                a.Count,
                a.SampleId))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.DisplayName)
            .ToList();

        TotalCount = Contributors.Sum(c => c.Count);
        return Page();
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;

namespace PartyPix.Web.Pages.E;

public class SlideshowModel(AppDbContext db) : PageModel
{
    public Event Event { get; private set; } = default!;
    public List<Guid> InitialMediaIds { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Slug == slug, ct);
        if (ev is null) return NotFound();
        Event = ev;

        InitialMediaIds = await db.Media
            .Where(m => m.EventId == ev.Id && m.Status == MediaStatus.Ready && m.Kind == MediaKind.Image)
            .OrderByDescending(m => m.CreatedAt)
            .Take(100)
            .Select(m => m.Id)
            .ToListAsync(ct);
        return Page();
    }
}

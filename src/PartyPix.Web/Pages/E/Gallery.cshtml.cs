using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using PartyPix.Web.Services;

namespace PartyPix.Web.Pages.E;

public class GalleryModel(AppDbContext db, GuestSessionAccessor guests) : PageModel
{
    public Event Event { get; private set; } = default!;
    public GuestSession Guest { get; private set; } = default!;
    public List<Media> Media { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Slug == slug, ct);
        if (ev is null) return NotFound();

        var guest = await guests.GetAsync(ev, ct);
        if (guest is null) return RedirectToPage("/E/Welcome", new { slug });

        Event = ev;
        Guest = guest;
        Media = await db.Media
            .Where(m => m.EventId == ev.Id && m.Status == MediaStatus.Ready)
            .OrderByDescending(m => m.CreatedAt)
            .Take(200)
            .ToListAsync(ct);
        return Page();
    }
}

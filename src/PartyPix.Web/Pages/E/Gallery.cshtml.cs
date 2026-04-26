using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using PartyPix.Web.Services;

namespace PartyPix.Web.Pages.E;

public class GalleryModel(AppDbContext db, GuestSessionAccessor guests) : PageModel
{
    public const int PageSize = 100;

    public Event Event { get; private set; } = default!;
    public GuestSession Guest { get; private set; } = default!;
    public List<GalleryItem> Items { get; private set; } = new();

    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public int TotalCount { get; private set; }

    public record GalleryItem(Guid Id, int Kind, string? UploaderName);

    // Bind the page-number query param as `p` rather than `page`, since
    // `page` is reserved by Razor Pages routing for selecting which page to
    // render — using it as a route value via asp-route-page produces broken
    // hrefs.
    public async Task<IActionResult> OnGetAsync(string slug, int? p, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Slug == slug, ct);
        if (ev is null) return NotFound();

        var guest = await guests.GetAsync(ev, ct);
        if (guest is null) return RedirectToPage("/E/Welcome", new { slug });

        Event = ev;
        Guest = guest;

        var ready = db.Media.Where(m => m.EventId == ev.Id && m.Status == MediaStatus.Ready);
        TotalCount = await ready.CountAsync(ct);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        CurrentPage = Math.Clamp(p ?? 1, 1, TotalPages);

        Items = await ready
            .OrderByDescending(m => m.CreatedAt)
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .Select(m => new GalleryItem(
                m.Id,
                (int)m.Kind,
                m.GuestSession != null ? m.GuestSession.DisplayName : null))
            .ToListAsync(ct);

        return Page();
    }
}

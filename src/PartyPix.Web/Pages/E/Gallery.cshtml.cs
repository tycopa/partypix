using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using PartyPix.Web.Services;

namespace PartyPix.Web.Pages.E;

public class GalleryModel(AppDbContext db, GuestSessionAccessor guests) : PageModel
{
    // 48 = 2 × 3 × 4 × 6, so every responsive grid breakpoint (2/3/4 cols)
    // fills complete rows.
    public const int PageSize = 48;

    public Event Event { get; private set; } = default!;
    public GuestSession Guest { get; private set; } = default!;

    /// <summary>
    /// Every Ready media id for this event (just id + kind + uploader).
    /// Sent to the client so the lightbox can navigate through items that are
    /// not on the current page without an extra fetch. When a `by` filter is
    /// active this only contains items uploaded by that guest.
    /// </summary>
    public List<GalleryItem> AllItems { get; private set; } = new();

    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public int TotalCount { get; private set; }
    public int PageOffset => (CurrentPage - 1) * PageSize;

    public Guid? FilteredByUploaderId { get; private set; }
    public string? FilteredByUploaderName { get; private set; }

    public record GalleryItem(Guid Id, int Kind, string? UploaderName, Guid? UploaderId);

    public async Task<IActionResult> OnGetAsync(string slug, int? p, Guid? by, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Slug == slug, ct);
        if (ev is null) return NotFound();

        var guest = await guests.GetAsync(ev, ct);
        if (guest is null) return RedirectToPage("/E/Welcome", new { slug });

        Event = ev;
        Guest = guest;

        var ready = db.Media.Where(m => m.EventId == ev.Id && m.Status == MediaStatus.Ready);
        if (by.HasValue)
        {
            ready = ready.Where(m => m.GuestSessionId == by.Value);
            FilteredByUploaderId = by.Value;
            FilteredByUploaderName = await db.GuestSessions
                .Where(g => g.Id == by.Value && g.EventId == ev.Id)
                .Select(g => g.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        AllItems = await ready
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new GalleryItem(
                m.Id,
                (int)m.Kind,
                m.GuestSession != null ? m.GuestSession.DisplayName : null,
                m.GuestSessionId))
            .ToListAsync(ct);

        TotalCount = AllItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        CurrentPage = Math.Clamp(p ?? 1, 1, TotalPages);

        return Page();
    }

    /// <summary>
    /// Page numbers to render in the pager: always page 1 and the last
    /// page, plus a window of ±2 around the current page. Callers should
    /// emit an ellipsis between any two adjacent numbers that aren't
    /// consecutive.
    /// </summary>
    public IEnumerable<int> PageWindow()
    {
        var pages = new SortedSet<int> { 1, TotalPages };
        for (var i = Math.Max(1, CurrentPage - 2); i <= Math.Min(TotalPages, CurrentPage + 2); i++)
            pages.Add(i);
        return pages;
    }
}

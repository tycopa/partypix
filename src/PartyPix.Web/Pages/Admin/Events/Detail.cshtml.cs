using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;

namespace PartyPix.Web.Pages.Admin.Events;

[Authorize]
public class DetailModel(
    AppDbContext db,
    UserManager<AppUser> users,
    IConfiguration config) : PageModel
{
    public Event Event { get; private set; } = default!;
    public int MediaCount { get; private set; }
    public int GuestCount { get; private set; }
    public List<RecentItem> RecentMedia { get; private set; } = new();
    public string ShareUrl { get; private set; } = "";

    public record RecentItem(Guid Id, int Kind, string? UploaderName);

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken ct)
    {
        var ev = await LoadAsync(slug, ct);
        if (ev is null) return NotFound();
        await HydrateAsync(ev, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSettingsAsync(
        string slug,
        [FromForm] bool GuestUploadsEnabled,
        [FromForm] bool GuestbookEnabled,
        [FromForm] bool LikesEnabled,
        [FromForm] bool CommentsEnabled,
        CancellationToken ct)
    {
        var ev = await LoadAsync(slug, ct);
        if (ev is null) return NotFound();

        ev.GuestUploadsEnabled = GuestUploadsEnabled;
        ev.GuestbookEnabled = GuestbookEnabled;
        ev.LikesEnabled = LikesEnabled;
        ev.CommentsEnabled = CommentsEnabled;
        await db.SaveChangesAsync(ct);

        return RedirectToPage(new { slug });
    }

    private async Task<Event?> LoadAsync(string slug, CancellationToken ct)
    {
        var userId = users.GetUserId(User)!;
        return await db.Events
            .FirstOrDefaultAsync(e => e.Slug == slug && e.HostUserId == userId, ct);
    }

    private async Task HydrateAsync(Event ev, CancellationToken ct)
    {
        Event = ev;
        MediaCount = await db.Media.CountAsync(m => m.EventId == ev.Id, ct);
        GuestCount = await db.GuestSessions.CountAsync(g => g.EventId == ev.Id, ct);
        RecentMedia = await db.Media
            .Where(m => m.EventId == ev.Id && m.Status == MediaStatus.Ready)
            .OrderByDescending(m => m.CreatedAt)
            .Take(60)
            .Select(m => new RecentItem(
                m.Id,
                (int)m.Kind,
                m.GuestSession != null ? m.GuestSession.DisplayName : null))
            .ToListAsync(ct);

        var baseUrl = config["PublicBaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        ShareUrl = $"{baseUrl}/e/{ev.Slug}";
    }
}

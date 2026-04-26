using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using PartyPix.Web.Services;

namespace PartyPix.Web.Pages.Admin.Events;

[Authorize]
public class DetailModel(
    AppDbContext db,
    UserManager<AppUser> users,
    IStorageService storage,
    VideoProcessor videoProcessor,
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

    public async Task<IActionResult> OnPostDeleteAsync(
        string slug,
        [FromForm] string confirmSlug,
        CancellationToken ct)
    {
        var ev = await LoadAsync(slug, ct);
        if (ev is null) return NotFound();

        // Typed-slug confirmation so an accidental submit can't destroy data.
        if (!string.Equals(confirmSlug, slug, StringComparison.Ordinal))
        {
            TempData["DeleteError"] = "Event name didn't match — nothing was deleted.";
            return RedirectToPage(new { slug });
        }

        // Pull the file keys before the DB cascade removes the Media rows.
        var mediaKeys = await db.Media
            .Where(m => m.EventId == ev.Id)
            .Select(m => new { m.StorageKey, m.DisplayKey, m.ThumbnailKey, m.PosterKey })
            .ToListAsync(ct);

        foreach (var row in mediaKeys)
        {
            foreach (var key in new[] { row.ThumbnailKey, row.DisplayKey, row.PosterKey, row.StorageKey })
            {
                if (string.IsNullOrEmpty(key)) continue;
                try { await storage.DeleteAsync(key, ct); }
                catch { /* best-effort; orphaned files are acceptable vs. failing the whole delete */ }
            }
        }

        db.Events.Remove(ev);
        await db.SaveChangesAsync(ct);
        return RedirectToPage("/Admin/Index");
    }

    /// <summary>
    /// Re-runs VideoProcessor against every Ready video for this event that
    /// is missing a thumbnail or playback variant. Used to backfill items
    /// uploaded before ffmpeg / the H.264 transcode were wired up.
    /// </summary>
    public async Task<IActionResult> OnPostRegenerateVideoArtifactsAsync(
        string slug, CancellationToken ct)
    {
        var ev = await LoadAsync(slug, ct);
        if (ev is null) return NotFound();

        var videos = await db.Media
            .Where(m => m.EventId == ev.Id
                        && m.Kind == MediaKind.Video
                        && m.Status == MediaStatus.Ready
                        && (m.ThumbnailKey == null
                            || m.PosterKey == null
                            || m.DisplayKey == null))
            .ToListAsync(ct);

        var processed = 0;
        var failed = 0;
        foreach (var m in videos)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(m.StorageKey) || !storage.Exists(m.StorageKey))
            {
                failed++;
                continue;
            }

            var thumbKey = $"events/{ev.Slug}/thumb/{m.Id}.jpg";
            var posterKey = $"events/{ev.Slug}/poster/{m.Id}.jpg";
            var displayKey = $"events/{ev.Slug}/display/{m.Id}.mp4";

            try
            {
                var info = await videoProcessor.ProcessAsync(
                    m.StorageKey, thumbKey, posterKey, displayKey, ct);

                if (storage.Exists(thumbKey)) m.ThumbnailKey = thumbKey;
                if (storage.Exists(posterKey)) m.PosterKey = posterKey;
                if (info.TranscodedToMp4 && storage.Exists(displayKey))
                    m.DisplayKey = displayKey;
                if (info.DurationSeconds.HasValue) m.DurationSeconds = info.DurationSeconds;
                processed++;
            }
            catch
            {
                failed++;
            }
        }

        await db.SaveChangesAsync(ct);
        TempData["RegenStatus"] = videos.Count == 0
            ? "No videos needed regeneration."
            : $"Regenerated {processed} video(s)" + (failed > 0 ? $", {failed} failed." : ".");
        return RedirectToPage(new { slug });
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

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using PartyPix.Web.Services;

namespace PartyPix.Web.Controllers;

/// <summary>
/// Gated media delivery. Files are never served from the storage root by
/// IIS/Kestrel directly; everything flows through here so we can check that
/// the requester has a guest session for the event (or is the host).
/// </summary>
[ApiController]
[Route("media")]
public class MediaController(
    AppDbContext db,
    IStorageService storage,
    GuestSessionAccessor guests) : ControllerBase
{
    [HttpDelete("{id:guid}")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var m = await db.Media.Include(x => x.Event).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null || m.Event is null) return NotFound();

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var isHost = m.Event.HostUserId == userId;
        var isAdmin = User.IsInRole("Admin");
        if (!isHost && !isAdmin) return Forbid();

        foreach (var key in new[] { m.ThumbnailKey, m.DisplayKey, m.PosterKey, m.StorageKey })
        {
            if (string.IsNullOrEmpty(key)) continue;
            try { await storage.DeleteAsync(key, ct); }
            catch { /* ignore individual file errors; row removal below still proceeds */ }
        }

        db.Media.Remove(m);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/thumb")]
    public Task<IActionResult> Thumb(Guid id, CancellationToken ct) => ServeAsync(id, Variant.Thumb, ct);

    [HttpGet("{id:guid}/display")]
    public Task<IActionResult> Display(Guid id, CancellationToken ct) => ServeAsync(id, Variant.Display, ct);

    [HttpGet("{id:guid}/original")]
    public Task<IActionResult> Original(Guid id, CancellationToken ct) => ServeAsync(id, Variant.Original, ct);

    private enum Variant { Thumb, Display, Original }

    private async Task<IActionResult> ServeAsync(Guid id, Variant variant, CancellationToken ct)
    {
        var m = await db.Media.Include(x => x.Event).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null || m.Event is null) return NotFound();
        if (m.Status != MediaStatus.Ready && m.Status != MediaStatus.Hidden) return NotFound();

        var isHost = User.Identity?.IsAuthenticated == true &&
                     m.Event.HostUserId == User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!isHost)
        {
            var session = await guests.GetAsync(m.Event, ct);
            if (session is null) return Unauthorized();
        }

        // Pick the right file + content-type for the requested variant. Videos
        // need the original bytes for Display (so the browser can play them),
        // but the JPEG poster for Thumb. Images use generated JPEG variants
        // for both.
        string key;
        string contentType;
        if (variant == Variant.Thumb)
        {
            key = m.ThumbnailKey ?? "";
            contentType = "image/jpeg";
        }
        else if (variant == Variant.Display)
        {
            if (m.Kind == MediaKind.Video)
            {
                key = m.StorageKey;
                contentType = m.ContentType;
            }
            else
            {
                key = m.DisplayKey ?? m.StorageKey;
                contentType = "image/jpeg";
            }
        }
        else
        {
            key = m.StorageKey;
            contentType = m.ContentType;
        }

        if (string.IsNullOrEmpty(key) || !storage.Exists(key)) return NotFound();

        var stream = storage.OpenRead(key);

        // Cache JPEG thumbs/displays aggressively; Cloudflare can cache them
        // at the edge. Don't cache video bytes — they're delivered with range
        // requests and browsers already handle their own caching.
        if (variant == Variant.Thumb || (variant == Variant.Display && m.Kind != MediaKind.Video))
            Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        return File(stream, contentType, enableRangeProcessing: true);
    }
}

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

        var key = variant switch
        {
            Variant.Thumb => m.ThumbnailKey ?? m.DisplayKey ?? m.StorageKey,
            Variant.Display => m.DisplayKey ?? m.StorageKey,
            _ => m.StorageKey,
        };
        if (string.IsNullOrEmpty(key) || !storage.Exists(key)) return NotFound();

        var stream = storage.OpenRead(key);
        var contentType = variant == Variant.Original ? m.ContentType : "image/jpeg";

        // Cache thumbs/displays aggressively; Cloudflare will cache them at the edge.
        if (variant != Variant.Original)
            Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        return File(stream, contentType, enableRangeProcessing: true);
    }
}

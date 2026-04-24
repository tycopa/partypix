using Microsoft.AspNetCore.SignalR;
using PartyPix.Web.Hubs;

namespace PartyPix.Web.Services;

public class MediaNotifier(IHubContext<SlideshowHub> hub)
{
    public Task BroadcastMediaAddedAsync(
        string eventSlug,
        Guid mediaId,
        string? uploaderName,
        int kind,
        CancellationToken ct = default) =>
        hub.Clients.Group(SlideshowHub.GroupName(eventSlug))
            .SendAsync("mediaAdded", new { mediaId, uploaderName, kind, at = DateTime.UtcNow }, ct);
}

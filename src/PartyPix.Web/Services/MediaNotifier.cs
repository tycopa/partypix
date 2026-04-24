using Microsoft.AspNetCore.SignalR;
using PartyPix.Web.Hubs;

namespace PartyPix.Web.Services;

public class MediaNotifier(IHubContext<SlideshowHub> hub)
{
    public Task BroadcastMediaAddedAsync(string eventSlug, Guid mediaId, CancellationToken ct = default) =>
        hub.Clients.Group(SlideshowHub.GroupName(eventSlug))
            .SendAsync("mediaAdded", new { mediaId, at = DateTime.UtcNow }, ct);
}

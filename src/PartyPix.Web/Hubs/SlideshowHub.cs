using Microsoft.AspNetCore.SignalR;

namespace PartyPix.Web.Hubs;

/// <summary>
/// Connected clients join a per-event group. When new media is ready, the server
/// broadcasts "mediaAdded" with the media id so gallery and slideshow views can
/// update live.
/// </summary>
public class SlideshowHub : Hub
{
    public Task JoinEvent(string eventSlug) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(eventSlug));

    public Task LeaveEvent(string eventSlug) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(eventSlug));

    public static string GroupName(string eventSlug) => $"event:{eventSlug}";
}

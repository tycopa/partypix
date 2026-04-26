using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;

namespace PartyPix.Web.Services;

/// <summary>
/// Resolves the current guest session from the cookie. Guests identify
/// themselves per-event via a cookie named guest_{eventId}.
/// </summary>
public class GuestSessionAccessor(AppDbContext db, IHttpContextAccessor http)
{
    public static string CookieName(Guid eventId) => $"gp_guest_{eventId:N}";

    public async Task<GuestSession?> GetAsync(Event ev, CancellationToken ct = default)
    {
        var ctx = http.HttpContext;
        if (ctx is null) return null;
        if (!ctx.Request.Cookies.TryGetValue(CookieName(ev.Id), out var token) ||
            string.IsNullOrWhiteSpace(token))
            return null;

        var session = await db.GuestSessions
            .FirstOrDefaultAsync(g => g.EventId == ev.Id && g.CookieToken == token, ct);

        if (session is not null)
        {
            session.LastSeenAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return session;
    }

    public async Task<GuestSession> CreateAsync(Event ev, string displayName, CancellationToken ct = default)
    {
        var ctx = http.HttpContext ?? throw new InvalidOperationException("No HttpContext");
        var token = SlugService.NewGuestToken();
        var session = new GuestSession
        {
            EventId = ev.Id,
            CookieToken = token,
            DisplayName = displayName.Trim(),
            UserAgent = ctx.Request.Headers.UserAgent.ToString(),
            IpHash = HashIp(ctx.Connection.RemoteIpAddress?.ToString() ?? ""),
        };
        db.GuestSessions.Add(session);
        await db.SaveChangesAsync(ct);

        WriteCookie(ctx, ev.Id, token);
        return session;
    }

    /// <summary>
    /// Attach the current device to an existing guest session by writing
    /// that session's cookie token here. Used by the Welcome page when a
    /// guest confirms they're returning under the same name on a new
    /// device — keeps their uploads attributed to a single GuestSession id
    /// instead of creating a new one with the same display name.
    /// </summary>
    public async Task AttachAsync(Event ev, GuestSession existing, CancellationToken ct = default)
    {
        var ctx = http.HttpContext ?? throw new InvalidOperationException("No HttpContext");
        existing.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        WriteCookie(ctx, ev.Id, existing.CookieToken);
    }

    private static void WriteCookie(HttpContext ctx, Guid eventId, string token)
    {
        ctx.Response.Cookies.Append(CookieName(eventId), token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(180),
            IsEssential = true,
        });
    }

    private static string HashIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "";
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(ip)));
    }
}

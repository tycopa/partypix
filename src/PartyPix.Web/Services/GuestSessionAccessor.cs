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

        ctx.Response.Cookies.Append(CookieName(ev.Id), token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(180),
            IsEssential = true,
        });

        return session;
    }

    private static string HashIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "";
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(ip)));
    }
}

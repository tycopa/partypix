namespace PartyPix.Web.Middleware;

/// <summary>
/// Promotes CF-Connecting-IP to HttpContext.Connection.RemoteIpAddress so that
/// rate limiting and IP-hash logic see the real client IP when we're behind a
/// Cloudflare Tunnel.
/// </summary>
public class CloudflareClientIpMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext ctx)
    {
        var header = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header) &&
            System.Net.IPAddress.TryParse(header, out var ip) &&
            ctx.Connection.RemoteIpAddress is { } proxy &&
            System.Net.IPAddress.IsLoopback(proxy))
        {
            ctx.Connection.RemoteIpAddress = ip;
        }
        await next(ctx);
    }
}

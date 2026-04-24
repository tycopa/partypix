using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Services;

namespace PartyPix.Web.Controllers;

[ApiController]
[Route("qr")]
public class QrController(AppDbContext db, QrService qr, IConfiguration config) : ControllerBase
{
    [HttpGet("{slug}.png")]
    public async Task<IActionResult> Png(string slug, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Slug == slug, ct);
        if (ev is null) return NotFound();

        var baseUrl = config["PublicBaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        var url = $"{baseUrl}/e/{ev.Slug}";
        var bytes = qr.RenderPng(url);
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(bytes, "image/png");
    }

    [HttpGet("{slug}.svg")]
    public async Task<IActionResult> Svg(string slug, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Slug == slug, ct);
        if (ev is null) return NotFound();

        var baseUrl = config["PublicBaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        var url = $"{baseUrl}/e/{ev.Slug}";
        var svg = qr.RenderSvg(url);
        Response.Headers.CacheControl = "public, max-age=86400";
        return Content(svg, "image/svg+xml");
    }
}

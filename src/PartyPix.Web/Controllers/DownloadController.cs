using System.IO.Compression;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using PartyPix.Web.Services;

namespace PartyPix.Web.Controllers;

/// <summary>
/// Streams every media original for a host's event as a ZIP. Avoids building
/// the archive on disk — we write directly to the response stream.
/// </summary>
[ApiController]
[Route("download")]
[Authorize]
public class DownloadController(AppDbContext db, IStorageService storage) : ControllerBase
{
    [HttpGet("{slug}.zip")]
    public async Task DownloadZip(string slug, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var ev = await db.Events.FirstOrDefaultAsync(
            e => e.Slug == slug && e.HostUserId == userId, ct);
        if (ev is null)
        {
            Response.StatusCode = 404;
            return;
        }

        var media = await db.Media
            .Where(m => m.EventId == ev.Id && m.Status == MediaStatus.Ready)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{slug}.zip\"";

        await using var archive = new ZipArchive(Response.Body, ZipArchiveMode.Create, leaveOpen: false);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in media)
        {
            ct.ThrowIfCancellationRequested();
            if (!storage.Exists(m.StorageKey)) continue;

            var entryName = MakeUnique(m.OriginalFileName, used);
            var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
            entry.LastWriteTime = new DateTimeOffset(m.CreatedAt, TimeSpan.Zero);

            await using var entryStream = entry.Open();
            await using var source = storage.OpenRead(m.StorageKey);
            await source.CopyToAsync(entryStream, ct);
        }
    }

    private static string MakeUnique(string name, HashSet<string> used)
    {
        if (used.Add(name)) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; i < 10000; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (used.Add(candidate)) return candidate;
        }
        return $"{Guid.NewGuid():N}{ext}";
    }
}

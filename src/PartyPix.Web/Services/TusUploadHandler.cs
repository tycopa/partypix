using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;

namespace PartyPix.Web.Services;

/// <summary>
/// Glue between tusdotnet and our media pipeline. On upload completion we move
/// the tus file into our storage layout, persist a Media row, process variants,
/// and notify subscribers.
/// </summary>
public class TusUploadHandler(
    AppDbContext db,
    IStorageService storage,
    ImageProcessor images,
    VideoProcessor videos,
    MediaNotifier notifier,
    GuestSessionAccessor guests,
    ILogger<TusUploadHandler> log)
{
    /// <summary>Metadata keys clients must send with each upload.</summary>
    public const string MetaFileName = "filename";
    public const string MetaContentType = "filetype";
    public const string MetaEventSlug = "eventSlug";
    public const string MetaAlbumId = "albumId";

    /// <summary>
    /// Header set on the final tus PATCH response when the upload was a
    /// duplicate of an existing media row (same event, same SHA-256 of the
    /// bytes). The Upload page reads this in tus-js-client's onAfterResponse
    /// to flip the queue entry to "already uploaded" instead of "done".
    /// </summary>
    public const string DuplicateHeader = "X-Upload-Duplicate";

    public async Task OnFileCompletedAsync(FileCompleteContext ctx)
    {
        var file = await ctx.GetFileAsync();
        var meta = await file.GetMetadataAsync(ctx.CancellationToken);
        var fileName = meta.TryGetValue(MetaFileName, out var fn) ? fn.GetString(System.Text.Encoding.UTF8) : "upload";
        var contentType = meta.TryGetValue(MetaContentType, out var ct) ? ct.GetString(System.Text.Encoding.UTF8) : "application/octet-stream";
        var slug = meta.TryGetValue(MetaEventSlug, out var es) ? es.GetString(System.Text.Encoding.UTF8) : null;

        if (string.IsNullOrWhiteSpace(slug))
        {
            log.LogWarning("Tus upload {Id} missing eventSlug metadata", file.Id);
            return;
        }

        var ev = await db.Events.FirstOrDefaultAsync(e => e.Slug == slug, ctx.CancellationToken);
        if (ev is null)
        {
            log.LogWarning("Tus upload {Id} for unknown event {Slug}", file.Id, slug);
            return;
        }

        if (!ev.GuestUploadsEnabled)
        {
            log.LogInformation("Tus upload {Id} rejected; uploads disabled for {Slug}", file.Id, slug);
            return;
        }

        var session = await guests.GetAsync(ev, ctx.CancellationToken);

        // Hash the bytes before we copy them anywhere; if this content has
        // already been uploaded to this event we skip the rest of the
        // pipeline and signal it to the client via a response header.
        string contentHash;
        await using (var src = await file.GetContentAsync(ctx.CancellationToken))
        {
            using var sha = SHA256.Create();
            var hashBytes = await sha.ComputeHashAsync(src, ctx.CancellationToken);
            contentHash = Convert.ToHexString(hashBytes);
        }

        var existing = await db.Media
            .Where(m => m.EventId == ev.Id && m.ContentHash == contentHash)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ctx.CancellationToken);

        if (existing is not null)
        {
            ctx.HttpContext.Response.Headers[DuplicateHeader] = existing.Value.ToString();
            // Drop the tus temp file — there's nothing to keep.
            try { await ((ITusTerminationStore)ctx.Store).DeleteFileAsync(file.Id, ctx.CancellationToken); }
            catch (Exception ex) { log.LogWarning(ex, "Failed to delete duplicate tus temp {Id}", file.Id); }
            return;
        }

        var kind = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            ? MediaKind.Video
            : MediaKind.Image;

        var mediaId = Guid.NewGuid();
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) ext = kind == MediaKind.Video ? ".mp4" : ".jpg";

        var origKey = $"events/{ev.Slug}/orig/{mediaId}{ext}";
        var thumbKey = $"events/{ev.Slug}/thumb/{mediaId}.jpg";
        var posterKey = $"events/{ev.Slug}/poster/{mediaId}.jpg";
        var displayImageKey = $"events/{ev.Slug}/display/{mediaId}.jpg";
        var displayVideoKey = $"events/{ev.Slug}/display/{mediaId}.mp4";

        // Persist original (re-read the tus stream; ComputeHashAsync above
        // consumed a separate handle).
        await using (var src = await file.GetContentAsync(ctx.CancellationToken))
        {
            await storage.SaveAsync(origKey, src, ctx.CancellationToken);
        }

        var media = new Media
        {
            Id = mediaId,
            EventId = ev.Id,
            GuestSessionId = session?.Id,
            Kind = kind,
            Status = MediaStatus.Processing,
            OriginalFileName = fileName,
            ContentType = contentType,
            SizeBytes = new FileInfo(storage.ResolvePath(origKey)).Length,
            StorageKey = origKey,
            ContentHash = contentHash,
        };
        db.Media.Add(media);
        await db.SaveChangesAsync(ctx.CancellationToken);

        try
        {
            if (kind == MediaKind.Image)
            {
                var info = await images.ProcessAsync(origKey, displayImageKey, thumbKey, ctx.CancellationToken);
                media.DisplayKey = displayImageKey;
                media.ThumbnailKey = thumbKey;
                media.Width = info.Width;
                media.Height = info.Height;
                media.TakenAt = info.TakenAt;
                media.Status = MediaStatus.Ready;
            }
            else
            {
                var info = await videos.ProcessAsync(
                    origKey, thumbKey, posterKey, displayVideoKey, ctx.CancellationToken);
                // ProcessAsync swallows ffmpeg failures, so check the files
                // actually exist before claiming we have them.
                if (storage.Exists(thumbKey)) media.ThumbnailKey = thumbKey;
                if (storage.Exists(posterKey)) media.PosterKey = posterKey;
                if (info.TranscodedToMp4 && storage.Exists(displayVideoKey))
                    media.DisplayKey = displayVideoKey;
                media.DurationSeconds = info.DurationSeconds;
                media.Status = MediaStatus.Ready;
            }

            await db.SaveChangesAsync(ctx.CancellationToken);
            await notifier.BroadcastMediaAddedAsync(
                ev.Slug, media.Id, session?.DisplayName, (int)kind, ctx.CancellationToken);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to process media {MediaId}", mediaId);
            media.Status = MediaStatus.Failed;
            await db.SaveChangesAsync(ctx.CancellationToken);
        }

        // Remove the tus temp file
        try { await ((ITusTerminationStore)ctx.Store).DeleteFileAsync(file.Id, ctx.CancellationToken); }
        catch (Exception ex) { log.LogWarning(ex, "Failed to delete tus temp {Id}", file.Id); }
    }
}

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

        var kind = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            ? MediaKind.Video
            : MediaKind.Image;

        var mediaId = Guid.NewGuid();
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) ext = kind == MediaKind.Video ? ".mp4" : ".jpg";

        var origKey = $"events/{ev.Slug}/orig/{mediaId}{ext}";
        var displayKey = $"events/{ev.Slug}/display/{mediaId}.jpg";
        var thumbKey = $"events/{ev.Slug}/thumb/{mediaId}.jpg";
        var posterKey = $"events/{ev.Slug}/poster/{mediaId}.jpg";

        // Persist original
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
        };
        db.Media.Add(media);
        await db.SaveChangesAsync(ctx.CancellationToken);

        try
        {
            if (kind == MediaKind.Image)
            {
                var info = await images.ProcessAsync(origKey, displayKey, thumbKey, ctx.CancellationToken);
                media.DisplayKey = displayKey;
                media.ThumbnailKey = thumbKey;
                media.Width = info.Width;
                media.Height = info.Height;
                media.TakenAt = info.TakenAt;
                media.Status = MediaStatus.Ready;
            }
            else
            {
                var info = await videos.ProcessAsync(origKey, thumbKey, posterKey, ctx.CancellationToken);
                // ProcessAsync swallows ffmpeg failures, so check the files
                // actually exist before claiming we have a thumbnail.
                if (storage.Exists(thumbKey)) media.ThumbnailKey = thumbKey;
                if (storage.Exists(posterKey)) media.PosterKey = posterKey;
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

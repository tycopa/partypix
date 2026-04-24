using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace PartyPix.Web.Services;

public record ProcessedImage(int Width, int Height, DateTime? TakenAt);

public class ImageProcessor(IStorageService storage, ILogger<ImageProcessor> log)
{
    private const int DisplayMaxEdge = 2000;
    private const int ThumbMaxEdge = 512;

    /// <summary>
    /// Reads the original, produces a display-sized and thumbnail variant, and strips GPS EXIF.
    /// Returns basic metadata for persistence.
    /// </summary>
    public async Task<ProcessedImage> ProcessAsync(
        string originalKey,
        string displayKey,
        string thumbKey,
        CancellationToken ct = default)
    {
        log.LogDebug("Processing image: originalKey={OriginalKey}", originalKey);
        await using var input = storage.OpenRead(originalKey);
        using var image = await Image.LoadAsync(input, ct);

        var takenAt = TryReadDateTaken(image);

        // Scrub GPS
        if (image.Metadata.ExifProfile is { } exif)
        {
            foreach (var tag in GpsExifTags)
                exif.RemoveValue(tag);
        }

        await SaveResizedAsync(image, displayKey, DisplayMaxEdge, ct);
        await SaveResizedAsync(image, thumbKey, ThumbMaxEdge, ct);

        return new ProcessedImage(image.Width, image.Height, takenAt);
    }

    private async Task SaveResizedAsync(Image image, string key, int maxEdge, CancellationToken ct)
    {
        using var clone = image.Clone(ctx =>
        {
            var w = image.Width;
            var h = image.Height;
            if (Math.Max(w, h) > maxEdge)
            {
                ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxEdge, maxEdge),
                });
            }
            ctx.AutoOrient();
        });

        var full = storage.ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        await clone.SaveAsJpegAsync(fs, ct);
    }

    private static DateTime? TryReadDateTaken(Image image)
    {
        var exif = image.Metadata.ExifProfile;
        if (exif is null) return null;
        if (exif.TryGetValue(ExifTag.DateTimeOriginal, out var v) &&
            DateTime.TryParseExact(v.Value, "yyyy:MM:dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal,
                out var dt))
        {
            return dt.ToUniversalTime();
        }
        return null;
    }

    private static readonly ExifTag[] GpsExifTags =
    [
        ExifTag.GPSLatitude,
        ExifTag.GPSLatitudeRef,
        ExifTag.GPSLongitude,
        ExifTag.GPSLongitudeRef,
        ExifTag.GPSAltitude,
        ExifTag.GPSAltitudeRef,
        ExifTag.GPSTimestamp,
        ExifTag.GPSDateStamp,
        ExifTag.GPSMapDatum,
        ExifTag.GPSProcessingMethod,
        ExifTag.GPSAreaInformation,
    ];
}

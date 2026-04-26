using System.Diagnostics;

namespace PartyPix.Web.Services;

public record ProcessedVideo(double? DurationSeconds, bool TranscodedToMp4);

/// <summary>
/// Pulls a poster frame and the playback duration out of an uploaded video
/// using the bundled (or system) ffmpeg/ffprobe binaries. Videos are served
/// raw from MediaController; this exists only to give them a thumbnail in
/// the grid and a poster for the &lt;video&gt; element.
/// </summary>
public class VideoProcessor(
    IStorageService storage,
    IWebHostEnvironment env,
    ILogger<VideoProcessor> log)
{
    // Prefer binaries shipped alongside the app under <ContentRoot>/tools so
    // hosts without a system ffmpeg install still get thumbnails. Falls back
    // to the bare name so the OS PATH lookup still works elsewhere.
    private readonly string _ffmpeg = ResolveBin(env, "ffmpeg");
    private readonly string _ffprobe = ResolveBin(env, "ffprobe");

    private static string ResolveBin(IWebHostEnvironment env, string name)
    {
        var exeName = OperatingSystem.IsWindows() ? $"{name}.exe" : name;
        var bundled = Path.Combine(env.ContentRootPath, "tools", exeName);
        return File.Exists(bundled) ? bundled : name;
    }

    /// <summary>
    /// Writes a 600x600 JPEG thumbnail (square cropped), a wider aspect-
    /// preserved poster JPEG, and — when the source isn't already H.264 — an
    /// H.264/AAC MP4 copy at <paramref name="displayKey"/> so Chrome/Edge can
    /// play iPhone HEVC clips. Each output is best-effort: if ffmpeg is
    /// missing or fails we log and skip that artifact rather than failing the
    /// upload.
    /// </summary>
    public async Task<ProcessedVideo> ProcessAsync(
        string originalKey,
        string thumbKey,
        string posterKey,
        string displayKey,
        CancellationToken ct = default)
    {
        var origPath = storage.ResolvePath(originalKey);
        var thumbPath = storage.ResolvePath(thumbKey);
        var posterPath = storage.ResolvePath(posterKey);
        var displayPath = storage.ResolvePath(displayKey);

        Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(posterPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(displayPath)!);

        var duration = await TryGetDurationAsync(origPath, ct);
        // Snap to half-duration for very short clips so we don't hit EOF.
        var seek = duration is { } d && d < 2 ? Math.Max(0, d / 2) : 1.0;
        var seekArg = seek.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        // Square-cropped thumb for the gallery grid.
        await TryRunFfmpegAsync(new[]
        {
            "-y", "-ss", seekArg, "-i", origPath,
            "-frames:v", "1",
            "-vf", "scale=600:600:force_original_aspect_ratio=increase,crop=600:600",
            "-q:v", "5",
            thumbPath,
        }, ct);

        // Aspect-preserved poster for the <video> element.
        await TryRunFfmpegAsync(new[]
        {
            "-y", "-ss", seekArg, "-i", origPath,
            "-frames:v", "1",
            "-vf", "scale='min(1280,iw)':-2",
            "-q:v", "4",
            posterPath,
        }, ct);

        // Transcode anything that isn't already H.264 to a baseline-compatible
        // MP4 so non-Safari browsers can play it. iPhone HEVC clips are the
        // common case. Skip when the codec is already h264 — even ffmpeg
        // ultrafast adds noticeable latency for nothing.
        var transcoded = false;
        var codec = await TryGetVideoCodecAsync(origPath, ct);
        var needsTranscode = !string.Equals(codec, "h264", StringComparison.OrdinalIgnoreCase);
        if (needsTranscode)
        {
            await TryRunFfmpegAsync(new[]
            {
                "-y", "-i", origPath,
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "23",
                "-pix_fmt", "yuv420p",
                "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
                "-c:a", "aac", "-b:a", "128k",
                "-movflags", "+faststart",
                displayPath,
            }, ct);
            transcoded = File.Exists(displayPath);
        }

        return new ProcessedVideo(duration, transcoded);
    }

    private async Task<string?> TryGetVideoCodecAsync(string path, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(_ffprobe)
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-select_streams");
            psi.ArgumentList.Add("v:0");
            psi.ArgumentList.Add("-show_entries");
            psi.ArgumentList.Add("stream=codec_name");
            psi.ArgumentList.Add("-of");
            psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
            psi.ArgumentList.Add(path);

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0 ? stdout.Trim() : null;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "ffprobe codec read failed for {Path}", path);
            return null;
        }
    }

    private async Task<double?> TryGetDurationAsync(string path, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(_ffprobe)
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-show_entries");
            psi.ArgumentList.Add("format=duration");
            psi.ArgumentList.Add("-of");
            psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
            psi.ArgumentList.Add(path);

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0) return null;
            return double.TryParse(stdout.Trim(),
                System.Globalization.CultureInfo.InvariantCulture,
                out var d) ? d : null;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "ffprobe failed for {Path}", path);
            return null;
        }
    }

    private async Task TryRunFfmpegAsync(IEnumerable<string> args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(_ffmpeg)
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc is null) return;

            // Drain both pipes so ffmpeg never blocks on a full buffer.
            var errTask = proc.StandardError.ReadToEndAsync(ct);
            var outTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var stderr = await errTask;
            _ = await outTask;
            if (proc.ExitCode != 0)
            {
                log.LogWarning("ffmpeg exited {Code}: {Err}",
                    proc.ExitCode, stderr.Length > 500 ? stderr[..500] : stderr);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "ffmpeg invocation failed");
        }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PartyPix.Web.Pages.Admin.Logs;

[Authorize(Roles = "Admin")]
public class IndexModel(IWebHostEnvironment env) : PageModel
{
    /// <summary>
    /// Tail size cap. Logs can grow to many MB; reading the whole file into a
    /// page would be wasteful — show the most recent ~256 KB and let admins
    /// download the full file if they need more.
    /// </summary>
    private const long TailBytes = 256 * 1024;

    public List<LogFileInfo> Files { get; private set; } = new();
    public string? Selected { get; private set; }
    public string Tail { get; private set; } = "";
    public bool Truncated { get; private set; }
    public string Filter { get; private set; } = "all";

    public record LogFileInfo(string Name, long Bytes, DateTime ModifiedUtc);

    public async Task OnGetAsync(string? file, string? filter, CancellationToken ct)
    {
        Filter = filter switch
        {
            "errors" => "errors",
            "warnings" => "warnings",
            _ => "all",
        };

        var logsDir = LogsDir();
        if (!Directory.Exists(logsDir)) return;

        Files = new DirectoryInfo(logsDir)
            .EnumerateFiles("partypix-*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new LogFileInfo(f.Name, f.Length, f.LastWriteTimeUtc))
            .ToList();

        Selected = ResolveFile(file) ?? Files.FirstOrDefault()?.Name;
        if (Selected is null) return;

        var path = Path.Combine(logsDir, Selected);
        if (!System.IO.File.Exists(path)) return;

        var info = new FileInfo(path);
        var startOffset = Math.Max(0, info.Length - TailBytes);
        Truncated = startOffset > 0;

        // Open with FileShare.ReadWrite | Delete so we don't fight Serilog,
        // which holds the active file open and may roll it mid-read.
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        fs.Seek(startOffset, SeekOrigin.Begin);
        using var reader = new StreamReader(fs);
        if (Truncated) await reader.ReadLineAsync(ct); // discard partial leading line

        var content = await reader.ReadToEndAsync(ct);

        if (Filter != "all")
        {
            var keep = Filter == "errors"
                ? new[] { "[ERR] ", "[FTL] " }
                : new[] { "[ERR] ", "[FTL] ", "[WRN] " };
            var matched = content
                .Split('\n')
                .Where(line => keep.Any(token => line.Contains(token, StringComparison.Ordinal)));
            content = string.Join('\n', matched);
        }

        Tail = content;
    }

    public IActionResult OnGetDownload(string file)
    {
        var name = ResolveFile(file);
        if (name is null) return NotFound();
        var path = Path.Combine(LogsDir(), name);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "text/plain", name);
    }

    private string LogsDir() => Path.Combine(env.ContentRootPath, "App_Data", "logs");

    /// <summary>
    /// Path-traversal guard. The picked file must be present in the logs
    /// directory listing (so any "../../whatever" attempts fall through to
    /// null and the page silently picks the latest).
    /// </summary>
    private string? ResolveFile(string? requested)
    {
        if (string.IsNullOrEmpty(requested)) return null;
        var dir = LogsDir();
        if (!Directory.Exists(dir)) return null;
        var match = new DirectoryInfo(dir)
            .EnumerateFiles("partypix-*.log")
            .FirstOrDefault(f => string.Equals(f.Name, requested, StringComparison.OrdinalIgnoreCase));
        return match?.Name;
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using PartyPix.Web.Services;

namespace PartyPix.Web.Pages.E;

public class UploadModel(AppDbContext db, GuestSessionAccessor guests, IConfiguration config) : PageModel
{
    public Event Event { get; private set; } = default!;
    public GuestSession Guest { get; private set; } = default!;

    /// <summary>
    /// Per-file ceiling enforced client-side before tus is even invoked. Set
    /// below Tus:MaxUploadBytes so a borderline file doesn't fail late in the
    /// upload after the user has waited on it.
    /// </summary>
    public long MaxFileBytes { get; private set; }

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Slug == slug, ct);
        if (ev is null) return NotFound();
        if (!ev.GuestUploadsEnabled) return RedirectToPage("/E/Gallery", new { slug });

        var guest = await guests.GetAsync(ev, ct);
        if (guest is null) return RedirectToPage("/E/Welcome", new { slug });

        Event = ev;
        Guest = guest;
        MaxFileBytes = config.GetValue<long?>("Upload:MaxFileBytes") ?? 400L * 1024 * 1024;
        return Page();
    }
}

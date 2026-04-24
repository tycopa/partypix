using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using PartyPix.Web.Services;

namespace PartyPix.Web.Pages.E;

public class WelcomeModel(AppDbContext db, GuestSessionAccessor guests) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Event Event { get; private set; } = default!;

    public class InputModel
    {
        [Required, StringLength(80, MinimumLength = 1)]
        public string DisplayName { get; set; } = "";

        [StringLength(64)]
        public string? AccessCode { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Slug == slug, ct);
        if (ev is null) return NotFound();
        Event = ev;

        // If guest already has a cookie session, skip ahead.
        var existing = await guests.GetAsync(ev, ct);
        if (existing is not null)
            return RedirectToPage("/E/Gallery", new { slug });

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string slug, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Slug == slug, ct);
        if (ev is null) return NotFound();
        Event = ev;

        if (!ModelState.IsValid) return Page();

        if (!string.IsNullOrEmpty(ev.AccessCode) &&
            !string.Equals(ev.AccessCode, Input.AccessCode?.Trim(), StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(Input.AccessCode), "Incorrect access code.");
            return Page();
        }

        await guests.CreateAsync(ev, Input.DisplayName, ct);
        return RedirectToPage("/E/Gallery", new { slug });
    }
}

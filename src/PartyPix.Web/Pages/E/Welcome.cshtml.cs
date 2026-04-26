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

    /// <summary>
    /// Set to true when an existing guest with that name was found and the
    /// page is asking the visitor whether they're the same person on a new
    /// device. The view renders a confirmation block in that case.
    /// </summary>
    public bool ShowSamePersonPrompt { get; private set; }

    public class InputModel
    {
        [Required, StringLength(80, MinimumLength = 1)]
        public string DisplayName { get; set; } = "";

        [StringLength(64)]
        public string? AccessCode { get; set; }

        /// <summary>
        /// Hidden field submitted by the "Yes, it's me" button. Without this,
        /// a name collision re-renders the page with the prompt instead of
        /// silently attaching the new device.
        /// </summary>
        public bool ConfirmSamePerson { get; set; }
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

        var trimmed = Input.DisplayName.Trim();

        // Match case-insensitively so "sarah" and "Sarah" both trigger the
        // prompt; spaces are already trimmed.
        var existing = await db.GuestSessions
            .Where(g => g.EventId == ev.Id && g.DisplayName.ToLower() == trimmed.ToLower())
            .OrderBy(g => g.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null && !Input.ConfirmSamePerson)
        {
            // Returning guest under the same name — ask before merging
            // them into the existing session.
            ShowSamePersonPrompt = true;
            return Page();
        }

        if (existing is not null && Input.ConfirmSamePerson)
        {
            await guests.AttachAsync(ev, existing, ct);
            return RedirectToPage("/E/Gallery", new { slug });
        }

        await guests.CreateAsync(ev, trimmed, ct);
        return RedirectToPage("/E/Gallery", new { slug });
    }
}

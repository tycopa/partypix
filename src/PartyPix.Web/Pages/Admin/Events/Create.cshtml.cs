using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using PartyPix.Web.Services;

namespace PartyPix.Web.Pages.Admin.Events;

[Authorize]
public class CreateModel(AppDbContext db, UserManager<AppUser> users) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, StringLength(200, MinimumLength = 2)]
        public string Title { get; set; } = "";

        [Required, DataType(DataType.Date)]
        public DateTime EventDate { get; set; } = DateTime.Today;

        [StringLength(2000)]
        public string? WelcomeMessage { get; set; }

        [StringLength(64)]
        public string? AccessCode { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        var userId = users.GetUserId(User)!;
        var slug = await GenerateUniqueSlugAsync(Input.Title, ct);

        var ev = new Event
        {
            HostUserId = userId,
            Slug = slug,
            Title = Input.Title.Trim(),
            WelcomeMessage = Input.WelcomeMessage?.Trim(),
            EventDate = DateTime.SpecifyKind(Input.EventDate.Date, DateTimeKind.Utc),
            AccessCode = string.IsNullOrWhiteSpace(Input.AccessCode) ? null : Input.AccessCode.Trim(),
        };

        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);

        return RedirectToPage("/Admin/Events/Detail", new { slug = ev.Slug });
    }

    private async Task<string> GenerateUniqueSlugAsync(string title, CancellationToken ct)
    {
        var baseSlug = SlugService.Slugify(title);
        var candidate = baseSlug;
        var attempt = 0;
        while (await db.Events.AnyAsync(e => e.Slug == candidate, ct))
        {
            attempt++;
            candidate = $"{baseSlug}-{SlugService.RandomSuffix(attempt < 3 ? 4 : 6)}";
            if (attempt > 8) throw new InvalidOperationException("Could not generate unique slug.");
        }
        return candidate;
    }
}

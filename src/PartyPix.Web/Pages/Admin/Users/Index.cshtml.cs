using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;

namespace PartyPix.Web.Pages.Admin.Users;

[Authorize(Roles = "Admin")]
public class IndexModel(UserManager<AppUser> userManager, AppDbContext db) : PageModel
{
    public List<UserRow> Users { get; private set; } = new();

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public record UserRow(string Id, string Email, bool IsAdmin, DateTime CreatedAt);

    public async Task OnGetAsync()
    {
        var all = userManager.Users
            .OrderBy(u => u.CreatedAt)
            .ToList();

        var rows = new List<UserRow>(all.Count);
        foreach (var u in all)
        {
            var isAdmin = await userManager.IsInRoleAsync(u, "Admin");
            rows.Add(new UserRow(
                u.Id,
                u.Email ?? u.UserName ?? "(unknown)",
                isAdmin,
                u.CreatedAt));
        }
        Users = rows;
    }

    public class ResetInput
    {
        [Required] public string UserId { get; set; } = default!;

        [Required]
        [StringLength(100, MinimumLength = 10, ErrorMessage = "Password must be at least 10 characters.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = default!;
    }

    public async Task<IActionResult> OnPostResetPasswordAsync([FromForm] ResetInput input)
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = string.Join("; ",
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToPage();
        }

        var user = await userManager.FindByIdAsync(input.UserId);
        if (user is null)
        {
            ErrorMessage = "User not found.";
            return RedirectToPage();
        }

        // Bypass the "old password" requirement: generate a reset token and
        // immediately consume it. This is the standard admin-reset pattern.
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, input.NewPassword);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join("; ", result.Errors.Select(e => e.Description));
        }
        else
        {
            StatusMessage = $"Password reset for {user.Email ?? user.UserName}.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteUserAsync([FromForm] string userId)
    {
        var current = await userManager.GetUserAsync(User);
        if (current is null) return Forbid();
        if (current.Id == userId)
        {
            ErrorMessage = "You can't delete your own account.";
            return RedirectToPage();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            ErrorMessage = "User not found.";
            return RedirectToPage();
        }

        // Refuse if the user still owns events: cascading user delete would
        // wipe events and media DB rows but orphan the storage files. Force
        // the admin to delete events first via the proper event-delete flow.
        var hasEvents = await db.Events.AnyAsync(e => e.HostUserId == userId);
        if (hasEvents)
        {
            ErrorMessage = $"{user.Email ?? user.UserName} still owns events. Delete those events first.";
            return RedirectToPage();
        }

        var result = await userManager.DeleteAsync(user);
        StatusMessage = result.Succeeded
            ? $"Deleted {user.Email ?? user.UserName}."
            : string.Join("; ", result.Errors.Select(e => e.Description));
        return RedirectToPage();
    }
}

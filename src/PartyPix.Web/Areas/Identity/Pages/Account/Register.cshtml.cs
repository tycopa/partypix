using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PartyPix.Web.Data.Entities;

namespace PartyPix.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterModel(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>True when no users exist yet; the first registration becomes the
    /// bootstrap admin and is publicly accessible. After that, registration is
    /// restricted to signed-in admins creating accounts for other hosts.</summary>
    public bool IsBootstrap { get; private set; }

    /// <summary>True when a signed-in admin is using this form to add a user.</summary>
    public bool IsAdminInvite { get; private set; }

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = default!;

        [Required]
        [StringLength(100, MinimumLength = 10, ErrorMessage = "Password must be at least 10 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = default!;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "Passwords don't match.")]
        public string ConfirmPassword { get; set; } = default!;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await ClassifyAsync();
        if (!IsBootstrap && !IsAdminInvite) return Forbid();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await ClassifyAsync();
        if (!IsBootstrap && !IsAdminInvite) return Forbid();
        if (!ModelState.IsValid) return Page();

        var user = new AppUser
        {
            UserName = Input.Email,
            Email = Input.Email,
        };

        var result = await userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError(string.Empty, err.Description);
            return Page();
        }

        if (IsBootstrap)
        {
            // First user ever — hand them the Admin role and sign them in.
            await userManager.AddToRoleAsync(user, "Admin");
            await signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect("~/Admin");
        }

        // Admin is inviting a new host account. Do not sign them in as the new
        // user — send the admin back to the dashboard.
        return LocalRedirect("~/Admin");
    }

    private async Task ClassifyAsync()
    {
        // Any user at all means bootstrap is over. Checking with Any() via
        // UserManager.Users keeps it cheap; no user enumeration is exposed.
        var anyUser = userManager.Users.Any();
        IsBootstrap = !anyUser;

        if (!IsBootstrap && User.Identity?.IsAuthenticated == true)
        {
            var currentUser = await userManager.GetUserAsync(User);
            IsAdminInvite = currentUser is not null &&
                            await userManager.IsInRoleAsync(currentUser, "Admin");
        }
    }
}

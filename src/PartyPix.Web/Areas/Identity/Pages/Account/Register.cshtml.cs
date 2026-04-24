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

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = new AppUser
        {
            UserName = Input.Email,
            Email = Input.Email,
        };

        var result = await userManager.CreateAsync(user, Input.Password);
        if (result.Succeeded)
        {
            await signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect("~/Admin");
        }

        foreach (var err in result.Errors)
            ModelState.AddModelError(string.Empty, err.Description);

        return Page();
    }
}

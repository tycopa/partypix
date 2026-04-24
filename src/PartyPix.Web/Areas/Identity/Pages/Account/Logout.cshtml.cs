using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PartyPix.Web.Data.Entities;

namespace PartyPix.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class LogoutModel(SignInManager<AppUser> signInManager) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return RedirectToPage("/Index");
    }
}

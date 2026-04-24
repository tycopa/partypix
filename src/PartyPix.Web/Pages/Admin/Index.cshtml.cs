using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;

namespace PartyPix.Web.Pages.Admin;

[Authorize]
public class IndexModel(AppDbContext db, UserManager<AppUser> users) : PageModel
{
    public List<Event> Events { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var userId = users.GetUserId(User)!;
        Events = await db.Events
            .Where(e => e.HostUserId == userId)
            .OrderByDescending(e => e.EventDate)
            .ToListAsync();
    }
}

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PartyPix.Web.Pages;

public class ErrorModel : PageModel
{
    public int? Code { get; set; }
    public string Message { get; set; } = "Something went wrong.";

    public void OnGet(int? code)
    {
        Code = code;
        Message = code switch
        {
            404 => "We couldn't find that page.",
            403 => "You don't have access to that.",
            _ => "Something went wrong.",
        };
    }
}

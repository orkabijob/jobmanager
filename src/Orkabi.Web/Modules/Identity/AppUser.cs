using Microsoft.AspNetCore.Identity;

namespace Orkabi.Web.Modules.Identity;

public class AppUser : IdentityUser<int>
{
    public string? FullName { get; set; }
}

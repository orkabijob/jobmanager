using Microsoft.AspNetCore.Identity;

namespace Orkabi.Web.Modules.Identity;

public class AppRole : IdentityRole<int>
{
    public AppRole() { }
    public AppRole(string name) : base(name) { }
}

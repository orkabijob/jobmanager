namespace Orkabi.Web.Modules.Identity;

/// <summary>Hebrew display names for the four fixed roles (used by the admin Users screens).</summary>
public static class RoleLabels
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        [AppRoles.Admin] = "מנהל",
        [AppRoles.CustomerService] = "שירות לקוחות",
        [AppRoles.Logistics] = "לוגיסטיקה",
        [AppRoles.Instructor] = "מדריך",
    };

    public static string Hebrew(string role) => Map.TryGetValue(role, out var he) ? he : role;
}

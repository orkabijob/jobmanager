using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Shared;

public enum NavSection { None, People, Curriculum, Scheduling, Operations, Logistics, Dashboard }

public sealed class PageShellVm
{
    public NavSection Section { get; init; } = NavSection.None;
    public string Title { get; init; } = "";
    public string? ActiveKey { get; init; }
    public string Greeting { get; init; } = "";
    public bool ShowSubnav { get; init; } = true;

    /// <summary>
    /// Optional override of the section's default subnav. Each item is (Label, Href, Roles) where
    /// Roles is a comma-separated allow-list (null = visible to everyone in the section); the shell
    /// hides items the current user is not in any of.
    /// </summary>
    public IReadOnlyList<(string Label, string Href, string? Roles)>? SubnavOverride { get; init; }

    public static IReadOnlyList<(string Label, string Href, string? Roles)> SubnavFor(NavSection section) =>
        section switch
        {
            NavSection.People => new[]
            {
                ("סקירה", "/People", (string?)null),
                ("בתי ספר", "/People/Schools", (string?)null),
                ("כיתות", "/People/Classes", (string?)null),
                ("לקוחות", "/People/Clients", (string?)null),
            },
            NavSection.Curriculum => new[]
            {
                ("סקירה", "/Curriculum", (string?)null),
                ("סילבוסים", "/Curriculum/Syllabi", (string?)null),
                ("דגמים", "/Curriculum/Models", (string?)null),
            },
            NavSection.Scheduling => new[]
            {
                ("סקירה", "/Scheduling", (string?)null),
                ("תבניות", "/Scheduling/Templates", (string?)null),
                ("מופעים", "/Scheduling/Instances", (string?)null),
                ("החלפות", "/Scheduling/Substitutions", (string?)null),
            },
            NavSection.Operations => new[]
            {
                ("סקירה", "/Operations", (string?)null),
                ("אישור שעות", "/Operations/ExtraHours", (string?)null),
                ("דיווחי אירוע", "/Operations/Incidents", (string?)null),
                ("אישור חופשות", "/Operations/Vacations", (string?)null),
                ("משימות פתוחות", "/Operations/ActionItems", (string?)null),
            },
            NavSection.Logistics => new[]
            {
                // Orders + PackingList are Logistics/Admin; MyOrders is the instructor surface.
                ("הזמנות", "/Logistics/Orders", (string?)AppRoles.LogisticsOrAdmin),
                ("ההזמנות של הכיתה שלי", "/Logistics/MyOrders", (string?)AppRoles.InstructorOrAdmin),
                ("רשימת אריזה", "/Logistics/PackingList", (string?)AppRoles.LogisticsOrAdmin),
            },
            _ => Array.Empty<(string, string, string?)>(),
        };

    public static string SectionAria(NavSection section) =>
        section switch
        {
            NavSection.People => "ניווט אנשים",
            NavSection.Curriculum => "ניווט תכנים",
            NavSection.Scheduling => "ניווט שיבוץ",
            NavSection.Operations => "ניווט תפעול",
            NavSection.Logistics => "ניווט לוגיסטיקה",
            NavSection.Dashboard => "ניווט",
            _ => "ניווט",
        };
}

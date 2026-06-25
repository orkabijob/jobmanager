namespace Orkabi.Web.Pages.Shared;

public enum NavSection { None, People, Curriculum, Scheduling, Operations, Logistics, Dashboard }

public sealed class PageShellVm
{
    public NavSection Section { get; init; } = NavSection.None;
    public string Title { get; init; } = "";
    public string? ActiveKey { get; init; }
    public string Greeting { get; init; } = "";
    public bool ShowSubnav { get; init; } = true;
    public IReadOnlyList<(string Label, string Href)>? SubnavOverride { get; init; }

    public static IReadOnlyList<(string Label, string Href)> SubnavFor(NavSection section) =>
        section switch
        {
            NavSection.People => new[]
            {
                ("סקירה", "/People"),
                ("בתי ספר", "/People/Schools"),
                ("כיתות", "/People/Classes"),
                ("לקוחות", "/People/Clients"),
            },
            NavSection.Curriculum => new[]
            {
                ("סקירה", "/Curriculum"),
                ("סילבוסים", "/Curriculum/Syllabi"),
                ("דגמים", "/Curriculum/Models"),
            },
            NavSection.Scheduling => new[]
            {
                ("סקירה", "/Scheduling"),
                ("תבניות", "/Scheduling/Templates"),
                ("מופעים", "/Scheduling/Instances"),
                ("החלפות", "/Scheduling/Substitutions"),
            },
            NavSection.Operations => new[]
            {
                ("סקירה", "/Operations"),
                ("אישור שעות", "/Operations/ExtraHours"),
                ("דיווחי אירוע", "/Operations/Incidents"),
                ("אישור חופשות", "/Operations/Vacations"),
                ("משימות פתוחות", "/Operations/ActionItems"),
            },
            NavSection.Logistics => new[]
            {
                ("הזמנות", "/Logistics/Orders"),
                ("רשימת אריזה", "/Logistics/PackingList"),
            },
            _ => Array.Empty<(string, string)>(),
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

namespace Orkabi.Web.Modules.Identity;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string CustomerService = "CustomerService";
    public const string Logistics = "Logistics";
    public const string Instructor = "Instructor";
    public static readonly string[] All = { Admin, CustomerService, Logistics, Instructor };
    public const string CsOrAdmin = Admin + "," + CustomerService;
    public const string InstructorOrAdmin = Admin + "," + Instructor;
    public const string LogisticsOrAdmin = Admin + "," + Logistics;
}

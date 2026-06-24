namespace Orkabi.Web.Modules.People;

public class Enrollment : Orkabi.Web.Shared.BaseEntity   // join; own lifecycle via EnrollmentStatus
{
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public int ClassId { get; set; }
    public Class Class { get; set; } = null!;
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;
    public bool IsTryout { get; set; }       // "started as a tryout" (historical); Status.Tryout = "currently in tryout"
    public bool PaidMaterials { get; set; }
    public bool PaidMonthly { get; set; }
    public DateTime EnrolledAt { get; set; } // business instant (UTC); set by the service at create, NOT the audit interceptor
}

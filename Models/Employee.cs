namespace OrgMgmt.Models
{
    public class Employee : Person
    {
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public decimal HourlyPayRate { get; set; }
        public List<Service> Services { get; set; } = [];
        public virtual ICollection<Shift> Shifts { get; set; } = new List<Shift>();
    }

}
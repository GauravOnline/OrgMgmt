namespace OrgMgmt.ViewModels
{
    public class EmployeeListViewModel
    {
        public List<EmployeeListItem> Employees { get; set; } = new();
    }

    public class EmployeeListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        // Note: No HourlyPayRate or financial data
    }
}

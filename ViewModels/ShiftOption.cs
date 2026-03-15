namespace OrgMgmt.ViewModels
{
    public class ShiftOption
    {
        public Guid ShiftId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string DaysOfWeek { get; set; } = string.Empty;
        public int Interval { get; set; }
    }
}

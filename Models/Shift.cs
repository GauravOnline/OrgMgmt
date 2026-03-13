using System;
using System.ComponentModel.DataAnnotations;

namespace OrgMgmt.Models
{
    public enum Frequency
    {
        Daily,
        Weekly,
        Monthly,
        Yearly
    }

    public class Shift
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string Name { get; set; } = string.Empty;

        public string Location { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Time)]
        public TimeSpan StartTime { get; set; }

        [Required]
        [DataType(DataType.Time)]
        public TimeSpan EndTime { get; set; }
        
        // Recurrence settings
        public Frequency Frequency { get; set; } = Frequency.Weekly;
        public int Interval { get; set; } = 1;                 // 1=Weekly, 2=Bi-weekly
        
        // For Weekly: Comma separated days e.g. "Monday,Wednesday"
        public string? DaysOfWeek { get; set; } 

        // Duration calculated property for display
        public TimeSpan Duration => EndTime - StartTime;

        // Many-to-Many relationship
        public virtual ICollection<Employee> Employees { get; set; } = new List<Employee>();
    }
}

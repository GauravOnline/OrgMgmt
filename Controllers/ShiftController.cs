using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrgMgmt.Models;

namespace OrgMgmt.Controllers
{
    public class ShiftController : Controller
    {
        private readonly OrgDbContext _context;

        public ShiftController(OrgDbContext context)
        {
            _context = context;
        }

        // GET: Shift
        public async Task<IActionResult> Index()
        {
            return View(await _context.Shifts.ToListAsync());
        }

        // GET: Shift/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Shift/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name,Location,StartTime,EndTime,Frequency,Interval,DaysOfWeek")] Shift shift)
        {
            if (ModelState.IsValid)
            {
                _context.Add(shift);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(shift);
        }

        // GET: Shift/Assign
        public async Task<IActionResult> Assign()
        {
            var model = new ShiftAssignmentViewModel
            {
                Employees = await _context.Employees
                    .Include(e => e.Shifts)
                    .ToListAsync(),
                Shifts = await _context.Shifts.ToListAsync()
            };
            return View(model);
        }

        // POST: Shift/Assign
        [HttpPost]
        public async Task<IActionResult> Assign(Dictionary<Guid, List<Guid>> EmployeeShifts)
        {
            // EmployeeShifts: Key = EmployeeId, Value = List of ShiftIds
            
            // 1. Get all employees involved (or all employees if we want to clear unchecked ones)
            // Ideally, we fetch all employees to be safe and clear those not present in the dict if the UI sends them.
            // But typical "Grid" might only send checked ones.
            // Better: Fetch all employees.
            var employees = await _context.Employees.Include(e => e.Shifts).ToListAsync();
            var allShifts = await _context.Shifts.ToListAsync();

            foreach (var employee in employees)
            {
                // Clear existing
                employee.Shifts.Clear();

                if (EmployeeShifts.ContainsKey(employee.ID))
                {
                    var shiftIds = EmployeeShifts[employee.ID];
                    // Validation: Check for overlaps within the proposed shifts
                    var proposedShifts = allShifts.Where(s => shiftIds.Contains(s.Id)).ToList();
                    
                    for (int i = 0; i < proposedShifts.Count; i++)
                    {
                        for (int j = i + 1; j < proposedShifts.Count; j++)
                        {
                            if (IsOverlapping(proposedShifts[i], proposedShifts[j]))
                            {
                                ModelState.AddModelError("", $"Employee {employee.Name} has overlapping shifts: {proposedShifts[i].Name} and {proposedShifts[j].Name}");
                                // Re-populate view model and return view
                                var model = new ShiftAssignmentViewModel
                                {
                                    Employees = await _context.Employees.Include(e => e.Shifts).ToListAsync(), // Reload original state
                                    Shifts = allShifts
                                };
                                return View(model);
                            }
                        }
                    }

                    foreach (var sId in shiftIds)
                    {
                        var shift = allShifts.FirstOrDefault(s => s.Id == sId);
                        if (shift != null)
                        {
                            employee.Shifts.Add(shift);
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Assign));
        }
    private bool IsOverlapping(Shift s1, Shift s2)
        {
            // Time overlap check
            bool timeOverlap = s1.StartTime < s2.EndTime && s2.StartTime < s1.EndTime;
            if (!timeOverlap) return false;

            // Simple Frequency Check (Enhance as needed)
            // If both are weekly, check days intersection
            if (s1.Frequency == Frequency.Weekly && s2.Frequency == Frequency.Weekly)
            {
                var days1 = s1.DaysOfWeek?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                var days2 = s2.DaysOfWeek?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                return days1.Intersect(days2).Any();
            }
            
            // If either is Daily (assuming simple model where Daily runs every day), then overlap is certain if times overlap
            if (s1.Frequency == Frequency.Daily || s2.Frequency == Frequency.Daily)
            {
                return true;
            }

            return false;
        }
    }

    public class ShiftAssignmentViewModel
    {
        public List<Employee> Employees { get; set; } = new();
        public List<Shift> Shifts { get; set; } = new();
    }
}

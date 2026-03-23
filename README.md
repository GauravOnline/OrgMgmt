# OrgMgmt - Organization Management System

A web application built with ASP.NET Core MVC for managing employees, shifts, and scheduling.

## Tech Stack

- **Framework**: ASP.NET Core MVC (.NET 8)
- **Database**: SQLite with Entity Framework Core
- **Frontend**: Bootstrap 5
- **IDE**: JetBrains Rider

## Features

### Employee Management
- Create, edit, delete employees
- Assign employees to services
- Track employment type (Full-Time / Part-Time)

### Shift Management
- Create and manage shifts with recurrence patterns (Weekly, BiWeekly, Monthly)
- Assign specific days of the week to shifts
- Set shift start/end dates

### Weekly Schedule Builder
- Visual weekly calendar (Mon-Sun)
- Assign shifts to employees on specific dates
- Remove shifts from schedule
- Track total weekly hours per employee
- Navigate between weeks

### Staff
- Store staff records with photo upload
- Track name, address, and date of birth
- Binary image storage in SQLite

## Getting Started

### Prerequisites
- .NET 8 SDK
- JetBrains Rider or Visual Studio

### Setup

1. Clone the repo:
   ```bash
   git clone https://github.com/GauravOnline/OrgMgmt
   cd OrgMgmt
   ```

2. Apply database migrations:
   ```bash
   dotnet ef database update
   ```

3. Run the app:
   ```bash
   dotnet run
   ```

4. Open your browser at `https://localhost:5001`

## Project Structure

```
OrgMgmt/
├── Controllers/
│   ├── EmployeesController.cs
│   ├── ShiftsController.cs
│   ├── StaffController.cs
│   ├── ClientsController.cs
│   └── ServicesController.cs
├── Models/
│   ├── Employee.cs
│   ├── Shift.cs
│   ├── ShiftAssignment.cs
│   ├── Staff.cs
│   ├── WeeklyScheduleViewModel.cs
│   └── OrgDbContext.cs
├── Views/
│   ├── Employees/
│   ├── Shifts/
│   ├── Staff/
│   └── Shared/
└── README.md
```

## Database Schema

- **Employees** - Employee records with employment type
- **Shifts** - Shift definitions with recurrence rules
- **ShiftAssignments** - Employee-shift assignments with specific dates
- **Services** - Service/department definitions
- **Clients** - Client records
- **Staff** - Staff records with photo storage

## Author

**Sarthak Agrawal**  
[GitHub](https://github.com/SarthakAgrawal442)

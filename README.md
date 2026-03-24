# OrgMgmt

OrgMgmt is an ASP.NET Core MVC application for managing employees, clients, services, shifts, schedule assignments, attendance adjustments, and payroll reporting in a role-based organization workflow.

## Tech Stack

- ASP.NET Core MVC on .NET 10
- Entity Framework Core 10 with SQLite
- ASP.NET Core Identity for authentication and authorization
- Bootstrap 5 for the UI
- ClosedXML for Excel export
- QuestPDF for PDF export
- xUnit, FluentAssertions, FsCheck, and Selenium for testing

## Implemented Features

### Authentication and authorization
- User registration, login, logout, and access-denied flows
- Role-based navigation and access control
- Seeded roles:
  - Admin
  - HR
  - Payroll
  - ScheduleManager
  - Employee
  - DirectManager
- Admin-only role assignment and removal
- Last-admin protection to prevent removing the final Admin role

### Employees
- Create, view, edit, and delete employees
- Store role, active status, hourly pay rate, address, and date of birth
- Optional employee photo upload
- Financial data visibility restricted by role

### Clients
- Create, view, edit, and delete clients
- Store balance, address, date of birth, and optional photo
- Client analysis page with:
  - number of clients by city
  - clients not registered for any service

### Services
- Create, view, edit, and delete services
- Link each service to an employee
- Assign multiple clients to a service through a many-to-many relationship

### Shifts and scheduling
- Create and manage shift definitions
- Store shift name, location, start time, end time, frequency, interval, and days of week
- Assign shifts to active employees through the scheduling workflow
- Remove assigned shifts from an employee
- Prevent overlapping assignments
- Prevent duplicate assignments
- Block assignments that conflict with approved vacation
- Show current assignments separately from available shifts

### Attendance
- Attendance dashboard by date
- Automatic dashboard rows for scheduled active employees
- Supported adjustment types:
  - None
  - Sick
  - Vacation
  - NoShow
  - Late
- Late-arrival validation requires a clock-in time
- Hours-to-pay calculation based on adjustment type
- Audit history for attendance changes

### Payroll
- Generate payroll reports for a selected date range
- Summarize regular, sick, vacation, late, and no-show values
- Calculate payable hours and gross pay from hourly rates
- Export payroll reports to:
  - Excel (`.xlsx`)
  - PDF (`.pdf`)

## Seeded Development Data

On startup, the app automatically applies migrations and seeds sample data for local development and testing.

Seeded accounts:
- `admin@orgmgmt.local` / `Admin123!`
- `hr@orgmgmt.local` / `Staff123!`
- `payroll@orgmgmt.local` / `Staff123!`
- `scheduler@orgmgmt.local` / `Staff123!`
- `manager@orgmgmt.local` / `Staff123!`
- `employee@orgmgmt.local` / `Staff123!`

Seeded sample data includes:
- active and inactive employees
- shifts and shift assignments
- clients and services
- attendance records
- attendance audit history

## Getting Started

### Prerequisites
- .NET 10 SDK
- Google Chrome installed for Selenium end-to-end tests

Note: the Selenium helper currently targets Chrome at `/usr/bin/google-chrome`, so the e2e tests assume a Linux environment with Chrome available at that path.

### Run the application

From the `OrgMgmt/` directory:

```bash
dotnet restore
dotnet run
```

The app applies migrations and seeds the database automatically on startup. The default SQLite database file is `app.db`.

Open the URL printed by `dotnet run` in the console output.

## Testing

From the `OrgMgmt/` directory:

### Run all tests

```bash
dotnet test OrgMgmt.Tests/OrgMgmt.Tests.csproj
```

### Implemented test suites

- `AccountControllerTests`
- `RoleSeederServiceTests`
- `CookieConfigurationTests`
- `FinancialDataAuthorizationServiceTests`
- `AttendanceServiceTests`
- `AttendanceControllerTests`
- `PayrollTests`
- `ServicesControllerTests`
- Property-based test suites in `OrgMgmt.Tests/Properties`
- Selenium end-to-end tests for User Stories 1, 2, and 3

### Run Selenium end-to-end tests

Regular e2e run:

```bash
dotnet test OrgMgmt.Tests/OrgMgmt.Tests.csproj --filter "Category=E2E"
```

Demo e2e run with visible browser and 2-second delay between user-visible actions:

```bash
E2E_DEMO_MODE=true E2E_STEP_DELAY_MS=2000 dotnet test OrgMgmt.Tests/OrgMgmt.Tests.csproj --filter "Category=E2E"
```

Run a single user story:

```bash
dotnet test OrgMgmt.Tests/OrgMgmt.Tests.csproj --filter "Category=UserStory1"
dotnet test OrgMgmt.Tests/OrgMgmt.Tests.csproj --filter "Category=UserStory2"
dotnet test OrgMgmt.Tests/OrgMgmt.Tests.csproj --filter "Category=UserStory3"
```

Run a single user story in demo mode:

```bash
E2E_DEMO_MODE=true E2E_STEP_DELAY_MS=2000 dotnet test OrgMgmt.Tests/OrgMgmt.Tests.csproj --filter "Category=UserStory1"
E2E_DEMO_MODE=true E2E_STEP_DELAY_MS=2000 dotnet test OrgMgmt.Tests/OrgMgmt.Tests.csproj --filter "Category=UserStory2"
E2E_DEMO_MODE=true E2E_STEP_DELAY_MS=2000 dotnet test OrgMgmt.Tests/OrgMgmt.Tests.csproj --filter "Category=UserStory3"
```

## Selenium End-to-End Story Flows

### User Story 1: Assigning Employee Schedules
- Scheduler logs in with `scheduler@orgmgmt.local`
- Opens the schedule assignment page
- Selects Alice Johnson
- Opens Manage Shifts
- Assigns the seeded `Weekend Day` bi-weekly shift
- Verifies the shift appears in current assignments
- Removes the shift and confirms it disappears

### User Story 2: Tracking and Adjusting Attendance
- HR user logs in with `hr@orgmgmt.local`
- Opens the attendance dashboard for a seeded Monday
- Selects Alice Johnson's attendance row
- Attempts a `Late Arrival` adjustment without a clock-in time
- Verifies the validation error appears
- Changes the adjustment to `No-Show`
- Saves the adjustment successfully
- Verifies payable hours become `0.00`
- Opens audit history and verifies the change is recorded

### User Story 3: Generating Bi-Weekly Payroll Reports
- Payroll user logs in with `payroll@orgmgmt.local`
- Opens the payroll page
- Verifies the default reporting period is pre-filled
- Generates the payroll report
- Verifies payroll rows appear
- Exports the report to Excel
- Exports the report to PDF

## Project Structure

- `Controllers/` - MVC controllers for app workflows
- `Models/` - domain entities
- `ViewModels/` - view-specific models
- `Services/` - business logic and validation
- `Views/` - Razor views
- `Migrations/` - EF Core migrations
- `OrgMgmt.Tests/` - unit, integration, property-based, and Selenium tests

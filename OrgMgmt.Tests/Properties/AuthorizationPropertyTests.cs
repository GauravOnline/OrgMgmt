using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrgMgmt.Controllers;
using Xunit;

namespace OrgMgmt.Tests.Properties;

// Feature: bi-weekly-schedule-generation, Property 10: Unauthorized role denial

/// <summary>
/// Property tests verifying that the ScheduleController restricts all actions
/// to users with the ScheduleManager (or Admin) role via [Authorize] attributes.
/// **Validates: Requirements 7.1, 7.2**
/// </summary>
public class AuthorizationPropertyTests
{
    private static readonly string[] NonScheduleManagerRoles =
        ["Employee", "HR", "Payroll", "DirectManager", "Viewer", "Guest", ""];

    /// <summary>
    /// For any user whose role is not "ScheduleManager" or "Admin", the controller's
    /// Authorize attribute SHALL deny access. We verify this structurally by checking
    /// that the controller-level [Authorize] attribute requires the correct roles.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UnauthorizedRole_IsDenied()
    {
        var roleGen = Gen.Elements(NonScheduleManagerRoles);

        return Prop.ForAll(roleGen.ToArbitrary(), role =>
        {
            // Get the Authorize attribute on the ScheduleController class
            var authorizeAttr = typeof(ScheduleController)
                .GetCustomAttribute<AuthorizeAttribute>();

            // The attribute must exist
            if (authorizeAttr == null) return false;

            // The Roles property must be set
            if (string.IsNullOrEmpty(authorizeAttr.Roles)) return false;

            var allowedRoles = authorizeAttr.Roles
                .Split(',', StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // The generated non-ScheduleManager role must NOT be in the allowed set
            return !allowedRoles.Contains(role);
        });
    }

    /// <summary>
    /// Verifies that all public action methods on ScheduleController are protected
    /// by the class-level Authorize attribute (no [AllowAnonymous] overrides).
    /// </summary>
    [Fact]
    public void AllActions_AreProtectedByAuthorize()
    {
        var controllerType = typeof(ScheduleController);

        // Class-level Authorize must exist
        var classAuth = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(classAuth);
        Assert.Contains("ScheduleManager", classAuth.Roles ?? "");

        // No action should have [AllowAnonymous]
        var actions = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(Task<IActionResult>) || m.ReturnType == typeof(IActionResult));

        foreach (var action in actions)
        {
            var allowAnon = action.GetCustomAttribute<AllowAnonymousAttribute>();
            Assert.Null(allowAnon);
        }
    }
}

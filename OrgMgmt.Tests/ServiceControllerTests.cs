using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrgMgmt;
using OrgMgmt.Controllers;
using OrgMgmt.Models;
using Xunit;

namespace OrgMgmt.Tests.Controllers
{
    public class ServicesControllerTests
    {
        // Creates a separate in-memory database for each test
        private OrgDbContext CreateContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<OrgDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;

            return new OrgDbContext(options);
        }

        // Creates a sample employee for testing
        private Employee CreateEmployee(string name = "Alice")
        {
            return new Employee
            {
                ID = Guid.NewGuid(),
                Name = name,
                Address = "Richmond",
                Role = "Care Aide",
                IsActive = true,
                HourlyPayRate = 25m
            };
        }

        // Creates a sample client for testing
        private Client CreateClient(string name = "Client One")
        {
            return new Client
            {
                ID = Guid.NewGuid(),
                Name = name,
                Address = "Vancouver",
                Balance = 0m
            };
        }

        // Creates a sample service for testing
        private Service CreateService(Guid employeeId, string type = "Care", decimal rate = 25m)
        {
            return new Service
            {
                Id = Guid.NewGuid(),
                Type = type,
                Rate = rate,
                EmployeeId = employeeId
            };
        }

        [Fact]
        public async Task Index_ReturnsView()
        {
            // Arrange
            using var context = CreateContext(nameof(Index_ReturnsView));

            var employee = CreateEmployee();
            var client = CreateClient();
            var service = CreateService(employee.ID);

            service.Employee = employee;
            service.Clients.Add(client);

            context.Employees.Add(employee);
            context.Clients.Add(client);
            context.Services.Add(service);
            await context.SaveChangesAsync();

            var controller = new ServicesController(context);

            // Act
            var result = await controller.Index();

            // Assert
            var viewResult = result.Should().BeOfType<ViewResult>().Subject;
            var model = viewResult.Model.Should()
                .BeAssignableTo<System.Collections.Generic.IEnumerable<Service>>().Subject;

            model.Should().HaveCount(1);

            var returnedService = model.First();
            returnedService.Type.Should().Be("Care");
            returnedService.Employee.Should().NotBeNull();
            returnedService.Clients.Should().HaveCount(1);
        }

        [Fact]
        public async Task Details_NullId_ReturnsNotFound()
        {
            // Arrange
            using var context = CreateContext(nameof(Details_NullId_ReturnsNotFound));
            var controller = new ServicesController(context);

            // Act
            var result = await controller.Details(null);

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task Details_MissingId_ReturnsNotFound()
        {
            // Arrange
            using var context = CreateContext(nameof(Details_MissingId_ReturnsNotFound));
            var controller = new ServicesController(context);

            // Act
            var result = await controller.Details(Guid.NewGuid());

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task Create_SavesService()
        {
            // Arrange
            using var context = CreateContext(nameof(Create_SavesService));

            var employee = CreateEmployee();
            var client1 = CreateClient("Client A");
            var client2 = CreateClient("Client B");

            context.Employees.Add(employee);
            context.Clients.AddRange(client1, client2);
            await context.SaveChangesAsync();

            var controller = new ServicesController(context);

            var service = new Service
            {
                Type = "Dietary",
                Rate = 40m,
                EmployeeId = employee.ID,
                Employee = employee
            };

            var selectedClients = new[] { client1.ID, client2.ID };

            // Act
            var result = await controller.Create(service, selectedClients);

            // Assert
            var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
            redirect.ActionName.Should().Be(nameof(ServicesController.Index));

            context.Services.Should().HaveCount(1);

            var savedService = await context.Services.Include(s => s.Clients).FirstAsync();
            savedService.Type.Should().Be("Dietary");
            savedService.Rate.Should().Be(40m);
            savedService.EmployeeId.Should().Be(employee.ID);
            savedService.Clients.Should().HaveCount(2);
            savedService.Clients.Select(c => c.ID)
                .Should().Contain(new[] { client1.ID, client2.ID });
        }

        [Fact]
        public async Task Create_EmptyClients_SavesService()
        {
            // Arrange
            using var context = CreateContext(nameof(Create_EmptyClients_SavesService));

            var employee = CreateEmployee();
            context.Employees.Add(employee);
            await context.SaveChangesAsync();

            var controller = new ServicesController(context);

            var service = new Service
            {
                Type = "Support",
                Rate = 22m,
                EmployeeId = employee.ID,
                Employee = employee
            };

            // Act
            var result = await controller.Create(service, Array.Empty<Guid>());

            // Assert
            result.Should().BeOfType<RedirectToActionResult>();

            var savedService = await context.Services.Include(s => s.Clients).FirstAsync();
            savedService.Clients.Should().BeEmpty();
        }

        [Fact]
        public async Task Create_InvalidModel_ReturnsView()
        {
            // Arrange
            using var context = CreateContext(nameof(Create_InvalidModel_ReturnsView));

            var employee = CreateEmployee();
            var client = CreateClient();

            context.Employees.Add(employee);
            context.Clients.Add(client);
            await context.SaveChangesAsync();

            var controller = new ServicesController(context);
            controller.ModelState.AddModelError("Type", "Type is required");

            var service = new Service
            {
                Type = "",
                Rate = 30m,
                EmployeeId = employee.ID,
                Employee = employee
            };

            var selectedClients = new[] { client.ID };

            // Act
            var result = await controller.Create(service, selectedClients);

            // Assert
            var viewResult = result.Should().BeOfType<ViewResult>().Subject;
            viewResult.Model.Should().BeSameAs(service);

            controller.ViewData["EmployeeId"].Should().NotBeNull();
            controller.ViewData["Clients"].Should().NotBeNull();

            context.Services.Should().BeEmpty();
        }

        [Fact]
        public async Task EditGet_NullId_ReturnsNotFound()
        {
            // Arrange
            using var context = CreateContext(nameof(EditGet_NullId_ReturnsNotFound));
            var controller = new ServicesController(context);

            // Act
            var result = await controller.Edit((Guid?)null);

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task EditGet_MissingService_ReturnsNotFound()
        {
            // Arrange
            using var context = CreateContext(nameof(EditGet_MissingService_ReturnsNotFound));
            var controller = new ServicesController(context);

            // Act
            var result = await controller.Edit(Guid.NewGuid());

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task EditGet_ReturnsView()
        {
            // Arrange
            using var context = CreateContext(nameof(EditGet_ReturnsView));

            var employee = CreateEmployee();
            var client1 = CreateClient("Client 1");
            var client2 = CreateClient("Client 2");
            var service = CreateService(employee.ID, "Nursing", 55m);

            service.Employee = employee;
            service.Clients.Add(client1);
            service.Clients.Add(client2);

            context.Employees.Add(employee);
            context.Clients.AddRange(client1, client2);
            context.Services.Add(service);
            await context.SaveChangesAsync();

            var controller = new ServicesController(context);

            // Act
            var result = await controller.Edit(service.Id);

            // Assert
            var viewResult = result.Should().BeOfType<ViewResult>().Subject;
            var model = viewResult.Model.Should().BeAssignableTo<Service>().Subject;

            model.Id.Should().Be(service.Id);
            controller.ViewData["EmployeeId"].Should().NotBeNull();
            controller.ViewData["Clients"].Should().NotBeNull();
        }

        [Fact]
        public async Task Edit_IdMismatch_ReturnsNotFound()
        {
            // Arrange
            using var context = CreateContext(nameof(Edit_IdMismatch_ReturnsNotFound));
            var controller = new ServicesController(context);

            var employee = CreateEmployee();
            var service = new Service
            {
                Id = Guid.NewGuid(),
                Type = "Care",
                Rate = 20m,
                EmployeeId = employee.ID,
                Employee = employee
            };

            // Act
            var result = await controller.Edit(Guid.NewGuid(), service, Array.Empty<Guid>());

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task Edit_UpdatesService()
        {
            // Arrange
            using var context = CreateContext(nameof(Edit_UpdatesService));

            var employee1 = CreateEmployee("Old Employee");
            var employee2 = CreateEmployee("New Employee");

            var oldClient = CreateClient("Old Client");
            var newClient1 = CreateClient("New Client 1");
            var newClient2 = CreateClient("New Client 2");

            var service = CreateService(employee1.ID, "Old Type", 10m);
            service.Employee = employee1;
            service.Clients.Add(oldClient);

            context.Employees.AddRange(employee1, employee2);
            context.Clients.AddRange(oldClient, newClient1, newClient2);
            context.Services.Add(service);
            await context.SaveChangesAsync();

            var controller = new ServicesController(context);

            var editedService = new Service
            {
                Id = service.Id,
                Type = "Updated Type",
                Rate = 99m,
                EmployeeId = employee2.ID,
                Employee = employee2
            };

            var selectedClients = new[] { newClient1.ID, newClient2.ID };

            // Act
            var result = await controller.Edit(service.Id, editedService, selectedClients);

            // Assert
            var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
            redirect.ActionName.Should().Be(nameof(ServicesController.Index));

            var updated = await context.Services
                .Include(s => s.Clients)
                .FirstAsync(s => s.Id == service.Id);

            updated.Type.Should().Be("Updated Type");
            updated.Rate.Should().Be(99m);
            updated.EmployeeId.Should().Be(employee2.ID);

            updated.Clients.Should().HaveCount(2);
            updated.Clients.Select(c => c.ID)
                .Should().Contain(new[] { newClient1.ID, newClient2.ID });
            updated.Clients.Select(c => c.ID)
                .Should().NotContain(oldClient.ID);
        }

        [Fact]
        public async Task Edit_EmptyClients_ClearsClients()
        {
            // Arrange
            using var context = CreateContext(nameof(Edit_EmptyClients_ClearsClients));

            var employee = CreateEmployee();
            var client = CreateClient();
            var service = CreateService(employee.ID, "Care", 20m);

            service.Employee = employee;
            service.Clients.Add(client);

            context.Employees.Add(employee);
            context.Clients.Add(client);
            context.Services.Add(service);
            await context.SaveChangesAsync();

            var controller = new ServicesController(context);

            var editedService = new Service
            {
                Id = service.Id,
                Type = "Care Updated",
                Rate = 25m,
                EmployeeId = employee.ID,
                Employee = employee
            };

            // Act
            var result = await controller.Edit(service.Id, editedService, Array.Empty<Guid>());

            // Assert
            result.Should().BeOfType<RedirectToActionResult>();

            var updated = await context.Services.Include(s => s.Clients).FirstAsync(s => s.Id == service.Id);
            updated.Clients.Should().BeEmpty();
        }

        [Fact]
        public async Task Edit_InvalidModel_ReturnsView()
        {
            // Arrange
            using var context = CreateContext(nameof(Edit_InvalidModel_ReturnsView));

            var employee = CreateEmployee();
            var client = CreateClient();

            context.Employees.Add(employee);
            context.Clients.Add(client);
            await context.SaveChangesAsync();

            var controller = new ServicesController(context);
            controller.ModelState.AddModelError("Type", "Type is required");

            var service = new Service
            {
                Id = Guid.NewGuid(),
                Type = "",
                Rate = 10m,
                EmployeeId = employee.ID,
                Employee = employee
            };

            var selectedClients = new[] { client.ID };

            // Act
            var result = await controller.Edit(service.Id, service, selectedClients);

            // Assert
            var viewResult = result.Should().BeOfType<ViewResult>().Subject;
            viewResult.Model.Should().BeSameAs(service);

            controller.ViewData["EmployeeId"].Should().NotBeNull();
            controller.ViewData["Clients"].Should().NotBeNull();
        }

        [Fact]
        public async Task DeleteGet_NullId_ReturnsNotFound()
        {
            // Arrange
            using var context = CreateContext(nameof(DeleteGet_NullId_ReturnsNotFound));
            var controller = new ServicesController(context);

            // Act
            var result = await controller.Delete(null);

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task DeleteGet_MissingService_ReturnsNotFound()
        {
            // Arrange
            using var context = CreateContext(nameof(DeleteGet_MissingService_ReturnsNotFound));
            var controller = new ServicesController(context);

            // Act
            var result = await controller.Delete(Guid.NewGuid());

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task Delete_RemovesService()
        {
            // Arrange
            using var context = CreateContext(nameof(Delete_RemovesService));

            var employee = CreateEmployee();
            var service = CreateService(employee.ID);
            service.Employee = employee;

            context.Employees.Add(employee);
            context.Services.Add(service);
            await context.SaveChangesAsync();

            var controller = new ServicesController(context);

            // Act
            var result = await controller.DeleteConfirmed(service.Id);

            // Assert
            var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
            redirect.ActionName.Should().Be(nameof(ServicesController.Index));

            context.Services.Should().BeEmpty();
        }

        [Fact]
        public async Task Delete_MissingService_Redirects()
        {
            // Arrange
            using var context = CreateContext(nameof(Delete_MissingService_Redirects));
            var controller = new ServicesController(context);

            // Act
            var result = await controller.DeleteConfirmed(Guid.NewGuid());

            // Assert
            var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
            redirect.ActionName.Should().Be(nameof(ServicesController.Index));
        }
    }
}
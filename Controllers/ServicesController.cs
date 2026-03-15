using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OrgMgmt;
using OrgMgmt.Models;

namespace OrgMgmt.Controllers
{
    [Authorize]
    public class ServicesController : Controller
    {
        private readonly OrgDbContext _context;

        public ServicesController(OrgDbContext context)
        {
            _context = context;
        }

        // GET: Services
        public async Task<IActionResult> Index()
        {
            // Added .Include(s => s.Clients) so the data is available to the view
            var services = _context.Services
                .Include(s => s.Employee)
                .Include(s => s.Clients); 
    
            return View(await services.ToListAsync());
        }

        // GET: Services/Details/5
        public async Task<IActionResult> Details(Guid? id)
        {
            if (id == null) return NotFound();

            var service = await _context.Services
                .Include(s => s.Employee)
                .Include(s => s.Clients) // Assignment Req: Show Many-to-Many
                .FirstOrDefaultAsync(m => m.Id == id);
            
            if (service == null) return NotFound();

            return View(service);
        }

        // GET: Services/Create
        public IActionResult Create()
        {
            // Use "Name", never "Discriminator"
            ViewData["EmployeeId"] = new SelectList(_context.Employees, "ID", "Name");
            ViewData["Clients"] = new MultiSelectList(_context.Clients, "ID", "Name");
            return View();
        }

        // POST: Services/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Type,Rate,EmployeeId")] Service service, Guid[] selectedClients)
        {
            ModelState.Remove("Employee");
            ModelState.Remove("Clients");
            if (ModelState.IsValid)
            {
                service.Id = Guid.NewGuid();

                // Logic to save Many-to-Many relationship
                if (selectedClients != null)
                {
                    foreach (var clientId in selectedClients)
                    {
                        var client = await _context.Clients.FindAsync(clientId);
                        if (client != null) service.Clients.Add(client);
                    }
                }

                _context.Add(service);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            // CRASH FIX: If we reach here, validation failed. We MUST refill BOTH lists.
            ViewData["EmployeeId"] = new SelectList(_context.Employees, "ID", "Name", service.EmployeeId);
            ViewData["Clients"] = new MultiSelectList(_context.Clients, "ID", "Name", selectedClients);
            
            return View(service);
        }

        // GET: Services/Edit/5
        public async Task<IActionResult> Edit(Guid? id)
        {
            if (id == null) return NotFound();

            var service = await _context.Services.Include(s => s.Clients).FirstOrDefaultAsync(s => s.Id == id);
            if (service == null) return NotFound();

            ViewData["EmployeeId"] = new SelectList(_context.Employees, "ID", "Name", service.EmployeeId);
            
            var selectedIds = service.Clients.Select(c => c.ID).ToList();
            ViewData["Clients"] = new MultiSelectList(_context.Clients, "ID", "Name", selectedIds);
            
            return View(service);
        }

        // POST: Services/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, [Bind("Id,Type,Rate,EmployeeId")] Service service, Guid[] selectedClients)
        {
            if (id != service.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var serviceToUpdate = await _context.Services.Include(s => s.Clients).FirstOrDefaultAsync(s => s.Id == id);
                    if (serviceToUpdate == null) return NotFound();

                    serviceToUpdate.Type = service.Type;
                    serviceToUpdate.Rate = service.Rate;
                    serviceToUpdate.EmployeeId = service.EmployeeId;

                    serviceToUpdate.Clients.Clear();
                    if (selectedClients != null)
                    {
                        foreach (var cid in selectedClients)
                        {
                            var client = await _context.Clients.FindAsync(cid);
                            if (client != null) serviceToUpdate.Clients.Add(client);
                        }
                    }

                    _context.Update(serviceToUpdate);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ServiceExists(service.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            
            // REFILL FOR EDIT
            ViewData["EmployeeId"] = new SelectList(_context.Employees, "ID", "Name", service.EmployeeId);
            ViewData["Clients"] = new MultiSelectList(_context.Clients, "ID", "Name", selectedClients);
            return View(service);
        }

        // GET: Services/Delete/5
        public async Task<IActionResult> Delete(Guid? id)
        {
            if (id == null) return NotFound();

            var service = await _context.Services
                .Include(s => s.Employee)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (service == null) return NotFound();

            return View(service);
        }

        // POST: Services/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var service = await _context.Services.FindAsync(id);
            if (service != null) _context.Services.Remove(service);

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ServiceExists(Guid id)
        {
            return _context.Services.Any(e => e.Id == id);
        }
    }
}
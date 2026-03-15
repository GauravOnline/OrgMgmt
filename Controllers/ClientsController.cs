using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrgMgmt.Models;

namespace OrgMgmt.Controllers
{
    [Authorize]
    public class ClientsController : Controller
    {
        private readonly OrgDbContext _context;

        public ClientsController(OrgDbContext context)
        {
            _context = context;
        }

        // GET: Clients
        public async Task<IActionResult> Index()
        {
            // Includes the Services so we can check relationships in the view
            return View(await _context.Clients.Include(c => c.Services).ToListAsync());
        }

        // GET: Clients/Analysis (New Action for your Assignment Queries)
        public async Task<IActionResult> Analysis()
        {
            // Query 1: Number of clients per city (Address) ordered by city name
            var cityStats = _context.Clients
                .GroupBy(c => c.Address)
                .Select(g => new CityReport 
                { 
                    City = g.Key ?? "Unknown", 
                    Count = g.Count() 
                })
                .OrderBy(x => x.City)
                .ToList();

            // Query 2: Clients who have NOT registered for a service
            var idleClients = await _context.Clients
                .Where(c => !c.Services.Any())
                .ToListAsync();

            // We use a ViewBag to send the city stats to the same page
            ViewBag.CityStats = cityStats;
            return View(idleClients);
        }

        // GET: Clients/Details/Guid
        public async Task<IActionResult> Details(Guid? id)
        {
            if (id == null) return NotFound();

            var client = await _context.Clients
                .Include(c => c.Services)
                .FirstOrDefaultAsync(m => m.ID == id);
            
            if (client == null) return NotFound();

            return View(client);
        }

        // GET: Clients/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Clients/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ID,Name,Address,DateOfBirth,Photo")] Client client, IFormFile? photoFile)
        {
            if (!ModelState.IsValid) return View(client);
            
            client.Address = char.ToUpper(client.Address[0]) + client.Address.Substring(1).ToLower();
            client.ID = Guid.NewGuid();
            
            if (photoFile != null && photoFile.Length > 0)
            {
                using (var memoryStream = new MemoryStream())
                {
                    await photoFile.CopyToAsync(memoryStream);
                    client.Photo = memoryStream.ToArray();
                }
            }
            
            _context.Add(client);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // GET: Clients/Edit/Guid
        public async Task<IActionResult> Edit(Guid? id)
        {
            if (id == null) return NotFound();

            var client = await _context.Clients.FindAsync(id);
            if (client == null) return NotFound();
            return View(client);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, [Bind("ID,Name,Address,DateOfBirth,Photo,Balance")] Client client)
        {
            if (id != client.ID) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(client);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ClientExists(client.ID)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(client);
        }

        // GET: Clients/Delete/Guid
        public async Task<IActionResult> Delete(Guid? id)
        {
            if (id == null) return NotFound();

            var client = await _context.Clients.FirstOrDefaultAsync(m => m.ID == id);
            if (client == null) return NotFound();

            return View(client);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var client = await _context.Clients.FindAsync(id);
            if (client != null) _context.Clients.Remove(client);
            
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ClientExists(Guid id)
        {
            return _context.Clients.Any(e => e.ID == id);
        }
    }

    // Small helper class for the City Report
    public class CityReport
    {
        public required string City { get; set; }
        public int Count { get; set; }
    }
}
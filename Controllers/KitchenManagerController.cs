using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantERP.Data;
using RestaurantERP.Models;
using RestaurantERP.Services;

namespace RestaurantERP.Controllers
{
    [Authorize(Roles = "Admin,Manager,Kitchen")]
    public class KitchenController : Controller
    {
        private readonly ApplicationDbContext _context;
        public KitchenController(ApplicationDbContext context) { _context = context; }

        public async Task<IActionResult> Index()
        {
            var orders = await _context.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Table)
                .Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing)
                .OrderBy(o => o.CreatedAt)
                .ToListAsync();
            return View(orders);
        }

        [HttpGet]
        public async Task<IActionResult> GetPendingOrders()
        {
            var orders = await _context.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Table)
                .Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.Ready)
                .OrderBy(o => o.CreatedAt)
                .Select(o => new
                {
                    o.Id,
                    o.OrderNumber,
                    o.CreatedAt,
                    status = o.Status.ToString(),
                    orderType = o.OrderType.ToString(),
                    o.Notes,
                    tableNumber = o.Table != null ? o.Table.TableNumber : null,
                    items = o.Items.Select(i => new
                    {
                        i.Id, i.Quantity, i.Notes,
                        productName = i.Product != null ? i.Product.Name : "Unknown",
                        productNameAr = i.Product != null ? i.Product.NameAr : "Unknown"
                    })
                })
                .ToListAsync();
            return Json(new { orders });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus([FromBody] KitchenUpdateStatusRequest req)
        {
            var order = await _context.Orders.FindAsync(req.OrderId);
            if (order == null) return Json(new { success = false, message = "Order not found" });
            if (!Enum.TryParse<OrderStatus>(req.Status, out var status))
                return Json(new { success = false, message = "Invalid status" });
            order.Status = status;
            if (status == OrderStatus.Completed) order.CompletedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }

    [Authorize(Roles = "Admin,Manager")]
    public class ManagerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AnalyticsService _analytics;
        public ManagerController(ApplicationDbContext context, AnalyticsService analytics)
        {
            _context = context;
            _analytics = analytics;
        }

        public async Task<IActionResult> Index()
        {
            var stats = await _analytics.GetDashboardStatsAsync();
            return View(stats);
        }

        public async Task<IActionResult> Orders(DateTime? from, DateTime? to)
        {
            from ??= DateTime.Today;
            to ??= DateTime.Today;
            var orders = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.Table)
                .Include(o => o.Cashier)
                .Where(o => o.CreatedAt.Date >= from.Value.Date && o.CreatedAt.Date <= to.Value.Date)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
            ViewBag.From = from.Value.ToString("yyyy-MM-dd");
            ViewBag.To = to.Value.ToString("yyyy-MM-dd");
            return View(orders);
        }

        public IActionResult Reports()
        {
            return View();
        }
    }

    public class KitchenUpdateStatusRequest
    {
        public int OrderId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}

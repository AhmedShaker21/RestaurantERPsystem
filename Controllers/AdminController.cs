using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantERP.Data;
using RestaurantERP.Models;
using RestaurantERP.Services;

namespace RestaurantERP.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly AnalyticsService _analytics;

        public AdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            AnalyticsService analytics)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _analytics = analytics;
        }

        public async Task<IActionResult> Index()
        {
            var stats = await _analytics.GetDashboardStatsAsync();
            return View(stats);
        }

        [HttpGet]
        public async Task<IActionResult> GetStats()
        {
            var stats = await _analytics.GetDashboardStatsAsync();
            return Json(stats);
        }

        public async Task<IActionResult> Products(string? search, int? categoryId, bool? active)
        {
            var query = _context.Products.Include(p => p.Category).AsQueryable();

            if (!string.IsNullOrEmpty(search))
                query = query.Where(p => p.Name.Contains(search) || p.NameAr.Contains(search));

            if (categoryId.HasValue)
                query = query.Where(p => p.CategoryId == categoryId);

            if (active.HasValue)
                query = query.Where(p => p.IsActive == active);

            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();
            ViewBag.Search = search;
            ViewBag.CategoryId = categoryId;

            return View(await query.OrderByDescending(p => p.CreatedAt).ToListAsync());
        }

        public async Task<IActionResult> CreateProduct()
        {
            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();

            return View(new Product
            {
                IsActive = true,
                IsAvailable = true,
                MinStockAlert = 2
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateProduct(Product model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();
                return View(model);
            }

            if (string.IsNullOrWhiteSpace(model.Barcode))
                model.Barcode = "P" + DateTime.Now.ToString("yyyyMMddHHmmss");

            model.CreatedAt = DateTime.Now;

            _context.Products.Add(model);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Product created successfully!";
            return RedirectToAction(nameof(Products));
        }

        public async Task<IActionResult> EditProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();
            return View(product);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProduct(int id, Product model)
        {
            if (id != model.Id) return NotFound();

            if (ModelState.IsValid)
            {
                model.UpdatedAt = DateTime.Now;
                _context.Update(model);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Product updated successfully!";
                return RedirectToAction(nameof(Products));
            }

            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return Json(new { success = false });

            var hasOrders = await _context.OrderItems.AnyAsync(oi => oi.ProductId == id);

            if (hasOrders)
            {
                product.IsActive = false;
                _context.Update(product);
            }
            else
            {
                _context.Products.Remove(product);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return Json(new { success = false });

            product.IsAvailable = !product.IsAvailable;
            await _context.SaveChangesAsync();

            return Json(new { success = true, isAvailable = product.IsAvailable });
        }

        public async Task<IActionResult> Categories()
        {
            var cats = await _context.Categories
                .Include(c => c.Products)
                .ToListAsync();

            return View(cats);
        }

        [HttpPost]
        public async Task<IActionResult> SaveCategory([FromBody] Category model)
        {
            if (model.Id == 0)
            {
                model.CreatedAt = DateTime.Now;
                _context.Categories.Add(model);
            }
            else
            {
                _context.Update(model);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, id = model.Id });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            var cat = await _context.Categories.FindAsync(id);
            if (cat == null) return Json(new { success = false });

            var hasProducts = await _context.Products.AnyAsync(p => p.CategoryId == id);
            if (hasProducts)
                return Json(new { success = false, message = "Category has products" });

            _context.Categories.Remove(cat);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        public async Task<IActionResult> Users()
        {
            var users = await _userManager.Users.ToListAsync();

            var model = new List<(ApplicationUser User, IList<string> Roles)>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                model.Add((user, roles));
            }

            ViewBag.Roles = await _roleManager.Roles
                .Select(r => r.Name!)
                .ToListAsync();

            return View(model);
        }
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
        {
            var user = new ApplicationUser
            {
                UserName = req.Email,
                Email = req.Email,
                FullName = req.FullName,
                FullNameAr = req.FullNameAr,
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(user, req.Password);

            if (!result.Succeeded)
                return Json(new { success = false, errors = result.Errors.Select(e => e.Description) });

            await _userManager.AddToRoleAsync(user, req.Role);
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleUser([FromBody] ToggleUserRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.UserId))
                return Json(new { success = false, message = "User id is required" });

            var user = await _userManager.FindByIdAsync(req.UserId);

            if (user == null)
                return Json(new { success = false, message = "User not found" });

            user.IsActive = !user.IsActive;

            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                return Json(new
                {
                    success = false,
                    message = string.Join(", ", result.Errors.Select(e => e.Description))
                });
            }

            return Json(new
            {
                success = true,
                isActive = user.IsActive
            });
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
        {
            var user = await _userManager.FindByIdAsync(req.UserId);
            if (user == null) return Json(new { success = false });

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, req.NewPassword);

            return Json(new { success = result.Succeeded });
        }

        public async Task<IActionResult> Orders(DateTime? from, DateTime? to, string? status)
        {
            from ??= DateTime.Today.AddDays(-30);
            to ??= DateTime.Today;

            var query = _context.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Table)
                .Include(o => o.Cashier)
                .Where(o => o.CreatedAt.Date >= from.Value.Date && o.CreatedAt.Date <= to.Value.Date);

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, out var s))
                query = query.Where(o => o.Status == s);

            ViewBag.From = from.Value.ToString("yyyy-MM-dd");
            ViewBag.To = to.Value.ToString("yyyy-MM-dd");
            ViewBag.Status = status;

            return View(await query.OrderByDescending(o => o.CreatedAt).ToListAsync());
        }

        public async Task<IActionResult> OrderDetails(int id)
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Table)
                .Include(o => o.Cashier)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            return View(order);
        }

        public async Task<IActionResult> Tables()
        {
            var tables = await _context.DiningTables
                .Include(t => t.Orders.Where(o =>
                    o.Status == OrderStatus.Pending ||
                    o.Status == OrderStatus.Preparing))
                .ToListAsync();

            return View(tables);
        }

        [HttpPost]
        public async Task<IActionResult> SaveTable([FromBody] DiningTable model)
        {
            if (model.Id == 0)
                _context.DiningTables.Add(model);
            else
                _context.Update(model);

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        public async Task<IActionResult> Expenses(DateTime? from, DateTime? to)
        {
            from ??= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            to ??= DateTime.Today;

            var expenses = await _context.Expenses
                .Include(e => e.CreatedBy)
                .Where(e => e.Date.Date >= from.Value.Date && e.Date.Date <= to.Value.Date)
                .OrderByDescending(e => e.Date)
                .ToListAsync();

            ViewBag.From = from.Value.ToString("yyyy-MM-dd");
            ViewBag.To = to.Value.ToString("yyyy-MM-dd");
            ViewBag.Total = expenses.Sum(e => e.Amount);

            return View(expenses);
        }

        [HttpPost]
        public async Task<IActionResult> SaveExpense([FromBody] SaveExpenseRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.Title) || req.Amount <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Title and amount are required"
                    });
                }

                var userId = _userManager.GetUserId(User);

                if (string.IsNullOrEmpty(userId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "User not found"
                    });
                }

                var currentShift = await _context.Shifts
                    .FirstOrDefaultAsync(s => s.UserId == userId && !s.IsClosed);

                if (currentShift == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "لازم تفتح وردية الأول"
                    });
                }

                var expense = new Expense
                {
                    Title = req.Title,
                    Amount = req.Amount,
                    Date = req.Date == default ? DateTime.Today : req.Date,
                    Category = req.Category,
                    PaymentMethod = req.PaymentMethod,
                    Description = req.Description,
                    CreatedById = userId,
                    ShiftId = currentShift.Id
                };

                _context.Expenses.Add(expense);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteExpense(int id)
        {
            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null) return Json(new { success = false });

            _context.Expenses.Remove(expense);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        public async Task<IActionResult> Inventory()
        {
            var products = await _context.Products
                .Include(p => p.Category)
                .Where(p => p.IsActive)
                .OrderBy(p => p.StockQuantity)
                .ToListAsync();

            return View(products);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStock([FromBody] UpdateStockRequest req)
        {
            var product = await _context.Products.FindAsync(req.ProductId);
            if (product == null) return Json(new { success = false });

            var before = product.StockQuantity;

            product.StockQuantity += req.Quantity;
            product.TrackStock = true;

            _context.InventoryLogs.Add(new InventoryLog
            {
                ProductId = req.ProductId,
                QuantityChange = req.Quantity,
                QuantityBefore = before,
                QuantityAfter = product.StockQuantity,
                Reason = req.Reason,
                CreatedAt = DateTime.Now,
                CreatedById = _userManager.GetUserId(User)
            });

            await _context.SaveChangesAsync();

            return Json(new { success = true, newStock = product.StockQuantity });
        }

        public async Task<IActionResult> Settings()
        {
            var settings = await _context.SystemSettings.ToListAsync();
            return View(settings.ToDictionary(s => s.Key, s => s.Value));
        }

        [HttpPost]
        public async Task<IActionResult> SaveSettings([FromBody] Dictionary<string, string> settings)
        {
            foreach (var kvp in settings)
            {
                var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == kvp.Key);

                if (setting != null)
                    setting.Value = kvp.Value;
                else
                    _context.SystemSettings.Add(new SystemSettings { Key = kvp.Key, Value = kvp.Value });
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        public async Task<IActionResult> Shifts()
        {
            var shifts = await _context.Shifts
                .Include(s => s.User)
                .Include(s => s.Orders)
                .OrderByDescending(s => s.StartTime)
                .Take(50)
                .ToListAsync();

            foreach (var shift in shifts)
            {
                shift.TotalSales = shift.Orders
                    .Where(o => o.Status == OrderStatus.Completed)
                    .Sum(o => o.Total);
            }

            return View(shifts);
        }
        public async Task<IActionResult> Reports()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardStats()
        {
            var stats = await _analytics.GetDashboardStatsAsync();
            return Json(stats);
        }

        [HttpGet]
        public async Task<IActionResult> GetRecentOrders(int count = 10)
        {
            var orders = await _context.Orders
                .Include(o => o.Cashier)
                .Include(o => o.Items)
                .Where(o => o.CreatedAt.Date == DateTime.Today)
                .OrderByDescending(o => o.CreatedAt)
                .Take(count)
                .Select(o => new
                {
                    o.Id,
                    o.OrderNumber,
                    o.OrderType,
                    o.Status,
                    o.Total,
                    o.PaymentMethod,
                    cashierName = o.Cashier != null ? o.Cashier.UserName : null,
                    itemCount = o.Items != null ? o.Items.Count : 0,
                    createdAt = o.CreatedAt
                })
                .ToListAsync();

            return Json(new { orders });
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveShifts()
        {
            var activeShifts = await _context.Shifts
                .Include(s => s.User)
                .Where(s => !s.IsClosed)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();

            var result = new List<object>();

            foreach (var shift in activeShifts)
            {
                var shiftOrders = await _context.Orders
                    .Where(o => o.ShiftId == shift.Id)
                    .ToListAsync();

                var completedOrders = shiftOrders
                    .Where(o => o.Status == OrderStatus.Completed)
                    .ToList();

                var totalSales = completedOrders.Sum(o => o.Total);
                var ordersCount = shiftOrders.Count;

                result.Add(new
                {
                    shift.Id,
                    cashierName = shift.User != null ? shift.User.UserName : null,
                    startTime = shift.StartTime.ToString("hh:mm tt"),
                    openingCash = shift.OpeningCash,
                    totalSales,
                    ordersCount,
                    durationMinutes = (int)(DateTime.Now - shift.StartTime).TotalMinutes
                });
            }

            return Json(new { shifts = result });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTableStatus([FromBody] UpdateTableStatusRequest req)
        {
            var table = await _context.DiningTables.FindAsync(req.TableId);

            if (table == null)
                return Json(new { success = false, message = "Table not found" });

            if (Enum.TryParse<TableStatus>(req.Status, out var status))
            {
                table.Status = status;
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }

            return Json(new { success = false, message = "Invalid status" });
        }

        [HttpGet]
        public async Task<IActionResult> GetReportData(int days = 30)
        {
            var from = DateTime.Today.AddDays(-days);
            var previousFrom = from.AddDays(-days);

            var completedOrders = await _context.Orders
                .Where(o => o.Status == OrderStatus.Completed && o.CreatedAt.Date >= from)
                .ToListAsync();

            var previousOrders = await _context.Orders
                .Where(o =>
                    o.Status == OrderStatus.Completed &&
                    o.CreatedAt.Date >= previousFrom &&
                    o.CreatedAt.Date < from)
                .ToListAsync();

            var totalRevenue = completedOrders.Sum(o => o.Total);
            var prevRevenue = previousOrders.Sum(o => o.Total);

            var revenueGrowth = prevRevenue > 0
                ? ((totalRevenue - prevRevenue) / prevRevenue * 100)
                : 0;

            var dailySales = completedOrders
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new
                {
                    date = g.Key.ToString("MM/dd"),
                    revenue = g.Sum(o => o.Total),
                    orders = g.Count()
                })
                .OrderBy(x => x.date)
                .ToList();

            var reportItems = await _context.OrderItems
                .Include(oi => oi.Order)
                .Include(oi => oi.Product)
                    .ThenInclude(p => p!.Category)
                .Where(oi =>
                    oi.Order != null &&
                    oi.Order.Status == OrderStatus.Completed &&
                    oi.Order.CreatedAt.Date >= from)
                .ToListAsync();

            var topProducts = reportItems
                .GroupBy(oi => oi.Product != null ? oi.Product.Name : "Unknown")
                .Select(g => new
                {
                    productName = g.Key,
                    totalQty = g.Sum(oi => oi.Quantity),
                    totalRevenue = g.Sum(oi => oi.TotalPrice)
                })
                .OrderByDescending(x => x.totalQty)
                .Take(10)
                .ToList();

            var categoryRevenue = reportItems
                .GroupBy(oi => oi.Product != null && oi.Product.Category != null
                    ? oi.Product.Category.Name
                    : "Other")
                .Select(g => new
                {
                    categoryName = g.Key,
                    revenue = g.Sum(oi => oi.TotalPrice)
                })
                .OrderByDescending(x => x.revenue)
                .ToList();

            var paymentMethods = completedOrders
                .GroupBy(o => o.PaymentMethod.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            var orderTypes = completedOrders
                .GroupBy(o => o.OrderType.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            return Json(new
            {
                totalRevenue,
                totalOrders = completedOrders.Count,
                avgOrderValue = completedOrders.Count > 0 ? totalRevenue / completedOrders.Count : 0,
                revenueGrowth,
                ordersGrowth = previousOrders.Count > 0
                    ? ((double)(completedOrders.Count - previousOrders.Count) / previousOrders.Count * 100)
                    : 0,
                dailySales,
                topProducts,
                categoryRevenue,
                paymentMethods,
                orderTypes
            });
        }
    }

    public class CreateUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string FullNameAr { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class UpdateTableStatusRequest
    {
        public int TableId { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class UpdateStockRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class SaveExpenseRequest
    {
        public string Title { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string Category { get; set; } = string.Empty;
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
        public string? Description { get; set; }
    }
    public class ToggleUserRequest
    {
        public string UserId { get; set; } = string.Empty;
    }
}
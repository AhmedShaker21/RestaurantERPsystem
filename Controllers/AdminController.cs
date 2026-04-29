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

        public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager, AnalyticsService analytics)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _analytics = analytics;
        }

        // ===== DASHBOARD =====
        public async Task<IActionResult> Index(int? branchId)
        {
            var branches = await _context.Branches.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
            var selectedBranch = branchId.HasValue ? branches.FirstOrDefault(b => b.Id == branchId) : null;

            ViewBag.Branches = branches;
            ViewBag.SelectedBranch = selectedBranch;
            ViewBag.SelectedBranchId = branchId;

            var stats = await _analytics.GetDashboardStatsAsync(branchId);
            return View(stats);
        }

        // ===== ANALYTICS API =====
        [HttpGet]
        public async Task<IActionResult> GetStats(int? branchId)
        {
            var stats = await _analytics.GetDashboardStatsAsync(branchId);
            return Json(stats);
        }

        // ===== PRODUCTS =====
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
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateProduct(Product model)
        {
            if (ModelState.IsValid)
            {
                model.CreatedAt = DateTime.Now;
                _context.Products.Add(model);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Product created successfully!";
                return RedirectToAction(nameof(Products));
            }
            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();
            return View(model);
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

        // ===== CATEGORIES =====
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
            if (hasProducts) return Json(new { success = false, message = "Category has products" });
            _context.Categories.Remove(cat);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ===== USERS =====
        public async Task<IActionResult> Users()
        {
            var users = await _userManager.Users
                .Include(u => u.DefaultBranch)
                .Include(u => u.UserBranches).ThenInclude(ub => ub.Branch)
                .ToListAsync();

            var userWithRoles = new List<(ApplicationUser User, IList<string> Roles, List<Branch> Branches)>();
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var branches = user.UserBranches?.Select(ub => ub.Branch!).Where(b => b != null).ToList()
                               ?? new List<Branch>();
                userWithRoles.Add((user, roles, branches));
            }

            ViewBag.Roles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
            ViewBag.Branches = await _context.Branches.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
            return View(userWithRoles);
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
                IsActive = true,
                DefaultBranchId = req.BranchIds?.FirstOrDefault() > 0
                                    ? req.BranchIds.First()
                                    : null
            };

            var result = await _userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return Json(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });

            await _userManager.AddToRoleAsync(user, req.Role);

            // Assign to branches
            if (req.BranchIds != null && req.BranchIds.Any())
            {
                bool first = true;
                foreach (var bid in req.BranchIds)
                {
                    _context.UserBranches.Add(new UserBranch
                    {
                        UserId = user.Id,
                        BranchId = bid,
                        IsPrimary = first
                    });
                    first = false;
                }
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true, userId = user.Id });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return Json(new { success = false });
            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);
            return Json(new { success = true, isActive = user.IsActive });
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

        // ===== ORDERS =====
        public async Task<IActionResult> Orders(DateTime? from, DateTime? to, string? status)
        {
            from ??= DateTime.Today.AddDays(-30);
            to ??= DateTime.Today;

            var query = _context.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
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
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Table)
                .Include(o => o.Cashier)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();
            return View(order);
        }

        // ===== TABLES =====
        public async Task<IActionResult> Tables()
        {
            var tables = await _context.DiningTables
                .Include(t => t.Orders.Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing))
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

        // ===== EXPENSES =====
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
        public async Task<IActionResult> SaveExpense([FromBody] Expense model)
        {
            var userId = _userManager.GetUserId(User);
            model.CreatedById = userId;
            if (model.Id == 0)
                _context.Expenses.Add(model);
            else
                _context.Update(model);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
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

        // ===== INVENTORY =====
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

        // ===== SETTINGS =====
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

        // ===== SHIFTS =====
        public async Task<IActionResult> Shifts()
        {
            var shifts = await _context.Shifts
                .Include(s => s.User)
                .Include(s => s.Branch)
                .OrderByDescending(s => s.StartTime)
                .Take(100)
                .ToListAsync();
            return View(shifts);
        }

        // ===== REPORTS =====
        public async Task<IActionResult> Reports()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UpdateUserBranches([FromBody] UpdateUserBranchesRequest req)
        {
            // Remove existing assignments
            var existing = await _context.UserBranches
                .Where(ub => ub.UserId == req.UserId).ToListAsync();
            _context.UserBranches.RemoveRange(existing);

            // Re-add new ones
            var primaryId = req.PrimaryBranchId ?? req.BranchIds.FirstOrDefault();
            foreach (var bid in req.BranchIds)
            {
                _context.UserBranches.Add(new UserBranch
                {
                    UserId = req.UserId,
                    BranchId = bid,
                    IsPrimary = bid == primaryId
                });
            }

            // Update default branch on user
            var user = await _userManager.FindByIdAsync(req.UserId) as ApplicationUser;
            if (user != null)
            {
                user.DefaultBranchId = req.BranchIds.Contains(primaryId) ? primaryId : req.BranchIds.FirstOrDefault();
                await _userManager.UpdateAsync(user);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> EditUser([FromBody] EditUserRequest req)
        {
            var user = await _userManager.FindByIdAsync(req.UserId) as ApplicationUser;
            if (user == null) return Json(new { success = false, message = "User not found" });

            user.FullName = req.FullName;
            user.FullNameAr = req.FullNameAr;
            if (req.Email != user.Email)
            {
                user.Email = req.Email;
                user.UserName = req.Email;
            }
            await _userManager.UpdateAsync(user);

            // Update role if changed
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (!currentRoles.Contains(req.Role))
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, req.Role);
            }

            return Json(new { success = true });
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
                .Select(o => new {
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
            var shifts = await _context.Shifts
                .Include(s => s.User)
                .Where(s => !s.IsClosed)
                .Select(s => new {
                    s.Id,
                    s.TotalSales,
                    cashierName = s.User != null ? s.User.UserName : null,
                    startTime = s.StartTime.ToString("hh:mm tt")
                })
                .ToListAsync();
            return Json(new { shifts });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTableStatus([FromBody] UpdateTableStatusRequest req)
        {
            var table = await _context.DiningTables.FindAsync(req.TableId);
            if (table == null) return Json(new { success = false, message = "Table not found" });
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

            var completedStatuses = new[] {
                OrderStatus.Completed,
                OrderStatus.Refunded,
                OrderStatus.PartialRefund
            };

            var completedOrders = await _context.Orders
                .Where(o => completedStatuses.Contains(o.Status) && o.CreatedAt.Date >= from)
                .ToListAsync();

            var previousOrders = await _context.Orders
                .Where(o => completedStatuses.Contains(o.Status) && o.CreatedAt.Date >= previousFrom && o.CreatedAt.Date < from)
                .ToListAsync();

            // Subtract refunds from revenue figures
            var periodRefunds = await _context.Refunds
                .Where(r => r.CreatedAt.Date >= from && r.Status == RefundStatus.Completed)
                .ToListAsync();

            var prevPeriodRefunds = await _context.Refunds
                .Where(r => r.CreatedAt.Date >= previousFrom && r.CreatedAt.Date < from && r.Status == RefundStatus.Completed)
                .ToListAsync();

            var grossRevenue = completedOrders.Sum(o => o.Total);
            var refundedAmount = periodRefunds.Sum(r => r.RefundTotal);
            var totalRevenue = Math.Max(0, grossRevenue - refundedAmount);

            var prevGross = previousOrders.Sum(o => o.Total);
            var prevRefunded = prevPeriodRefunds.Sum(r => r.RefundTotal);
            var prevRevenue = Math.Max(0, prevGross - prevRefunded);

            var revenueGrowth = prevRevenue > 0 ? ((totalRevenue - prevRevenue) / prevRevenue * 100) : 0;

            // Daily sales with refunds subtracted per day
            var refundByDay = periodRefunds
                .GroupBy(r => r.CreatedAt.Date)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.RefundTotal));

            var dailySales = completedOrders
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new {
                    date = g.Key.ToString("MM/dd"),
                    revenue = Math.Max(0, g.Sum(o => o.Total) - refundByDay.GetValueOrDefault(g.Key, 0)),
                    orders = g.Count()
                })
                .OrderBy(x => x.date)
                .ToList();

            var topProducts = await _context.OrderItems
                .Include(oi => oi.Product)
                .Where(oi => completedStatuses.Contains(oi.Order!.Status) && oi.Order.CreatedAt.Date >= from)
                .GroupBy(oi => oi.ProductName)
                .Select(g => new { productName = g.Key, totalQty = g.Sum(oi => oi.Quantity), totalRevenue = g.Sum(oi => oi.TotalPrice) })
                .OrderByDescending(x => x.totalQty)
                .Take(10)
                .ToListAsync();

            var categoryRevenue = await _context.OrderItems
                .Include(oi => oi.Product).ThenInclude(p => p!.Category)
                .Where(oi => completedStatuses.Contains(oi.Order!.Status) && oi.Order.CreatedAt.Date >= from)
                .GroupBy(oi => oi.Product != null && oi.Product.Category != null ? oi.Product.Category.Name : "Other")
                .Select(g => new { categoryName = g.Key, revenue = g.Sum(oi => oi.TotalPrice) })
                .OrderByDescending(x => x.revenue)
                .ToListAsync();

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
                ordersGrowth = previousOrders.Count > 0 ? ((double)(completedOrders.Count - previousOrders.Count) / previousOrders.Count * 100) : 0,
                totalRefunded = refundedAmount,
                dailySales,
                topProducts,
                categoryRevenue,
                paymentMethods,
                orderTypes
            });
        }

    } // end AdminController

    // ── UpdateUserBranches request ─────────────────────────────
    public class EditUserRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string FullNameAr { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class UpdateUserBranchesRequest
    {
        public string UserId { get; set; } = string.Empty;
        public List<int> BranchIds { get; set; } = new();
        public int? PrimaryBranchId { get; set; }
    }

    // Request Models
    public class CreateUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string FullNameAr { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public List<int> BranchIds { get; set; } = new();
        public int? PrimaryBranchId { get; set; }
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
}
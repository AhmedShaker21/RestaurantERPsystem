using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantERP.Data;
using RestaurantERP.Models;
using RestaurantERP.Services;

namespace RestaurantERP.Controllers
{
    [Authorize(Roles = "Admin,محصل")]
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

        // Helper: get branch id for current user (no BranchService needed)
        private async Task<int> GetBranchIdAsync()
        {
            var userId = _userManager.GetUserId(User);
            var user = await _context.Users
                .OfType<ApplicationUser>()
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.DefaultBranchId != null)
                return user.DefaultBranchId.Value;
            var userBranch = await _context.UserBranches
                .Where(ub => ub.UserId == userId)
                .Select(ub => ub.BranchId)
                .FirstOrDefaultAsync();
            if (userBranch > 0) return userBranch;
            return await _context.Branches
                .Where(b => b.IsMainBranch)
                .Select(b => b.Id)
                .FirstOrDefaultAsync();
        }

        // ═════════════════════════════════════════════════════
        // DASHBOARD
        // ═════════════════════════════════════════════════════
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Index(int? branchId)
        {
            var branches = await _context.Branches
                .Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
            var selectedBranch = branchId.HasValue
                ? branches.FirstOrDefault(b => b.Id == branchId) : null;

            ViewBag.Branches = branches;
            ViewBag.SelectedBranch = selectedBranch;
            ViewBag.SelectedBranchId = branchId;

            var stats = await _analytics.GetDashboardStatsAsync(branchId);
            return View(stats);
        }

        [HttpGet]
        public async Task<IActionResult> GetStats(int? branchId)
        {
            var stats = await _analytics.GetDashboardStatsAsync(branchId);
            return Json(stats);
        }

        // ═════════════════════════════════════════════════════
        // PRODUCTS
        // ═════════════════════════════════════════════════════
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

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateProduct(int? branchId)
        {
            var branches = await _context.Branches.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();
            ViewBag.Branches = branches;
            ViewBag.DefaultBranchId = branchId
                ?? await _context.Branches.Where(b => b.IsMainBranch).Select(b => (int?)b.Id).FirstOrDefaultAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateProduct(Product model, [FromForm] List<int> selectedBranchIds)
        {
            if (string.IsNullOrEmpty(model.Name)) model.Name = model.NameAr;

            // Auto-compute unit price from box price if SellByBox
            if (model.SellByBox && model.UnitsPerBox > 0)
            {
                model.Price = Math.Round(model.BoxSellPrice / model.UnitsPerBox, 2);
                model.CostPrice = Math.Round(model.BoxCostPrice / model.UnitsPerBox, 2);
            }

            // Clear validation errors for fields we compute server-side
            ModelState.Remove(nameof(model.Price));
            ModelState.Remove(nameof(model.CostPrice));
            ModelState.Remove(nameof(model.Name));
            ModelState.Remove(nameof(model.CategoryId));

            if (ModelState.IsValid)
            {
                model.CreatedAt = DateTime.Now;
                _context.Products.Add(model);
                await _context.SaveChangesAsync();

                // Assign branches (many-to-many)
                if (!selectedBranchIds.Any())
                {
                    // Fallback: assign to main branch
                    var mainId = await _context.Branches.Where(b => b.IsMainBranch).Select(b => b.Id).FirstOrDefaultAsync();
                    if (mainId > 0) selectedBranchIds.Add(mainId);
                }
                foreach (var bid in selectedBranchIds.Distinct())
                    _context.ProductBranches.Add(new ProductBranch { ProductId = model.Id, BranchId = bid });
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم إضافة المنتج بنجاح!";
                return RedirectToAction(nameof(Products));
            }
            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();
            ViewBag.Branches = await _context.Branches.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
            return View(model);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> EditProduct(int id)
        {
            var product = await _context.Products
                .Include(p => p.ProductBranches)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product == null) return NotFound();
            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();
            ViewBag.Branches = await _context.Branches.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
            ViewBag.SelectedBranchIds = product.ProductBranches.Select(pb => pb.BranchId).ToList();
            return View(product);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProduct(int id, Product model, [FromForm] List<int> selectedBranchIds)
        {
            if (id != model.Id) return NotFound();
            if (string.IsNullOrEmpty(model.Name)) model.Name = model.NameAr;
            if (model.SellByBox && model.UnitsPerBox > 0)
            {
                model.Price = Math.Round(model.BoxSellPrice / model.UnitsPerBox, 2);
                model.CostPrice = Math.Round(model.BoxCostPrice / model.UnitsPerBox, 2);
            }
            ModelState.Remove(nameof(model.Price));
            ModelState.Remove(nameof(model.CostPrice));
            ModelState.Remove(nameof(model.Name));
            ModelState.Remove(nameof(model.CategoryId));
            if (ModelState.IsValid)
            {
                model.UpdatedAt = DateTime.Now;
                _context.Update(model);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم تحديث المنتج بنجاح!";
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

        // ═════════════════════════════════════════════════════
        // CATEGORIES
        // ═════════════════════════════════════════════════════
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
            if (model == null)
                return Json(new { success = false, message = "بيانات غير صحيحة" });

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
                return Json(new { success = false, message = "لا يمكن حذف فئة بها منتجات" });
            _context.Categories.Remove(cat);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ═════════════════════════════════════════════════════
        // USERS
        // ═════════════════════════════════════════════════════
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
                var branches = user.UserBranches?
                    .Select(ub => ub.Branch!).Where(b => b != null).ToList()
                    ?? new List<Branch>();
                userWithRoles.Add((user, roles, branches));
            }

            ViewBag.Roles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
            ViewBag.Branches = await _context.Branches
                .Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
            return View(userWithRoles);
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
        {
            if (req == null)
                return Json(new { success = false, message = "بيانات غير صحيحة" });

            var user = new ApplicationUser
            {
                UserName = req.Email,
                Email = req.Email,
                FullName = req.FullName,
                FullNameAr = req.FullNameAr,
                EmailConfirmed = true,
                IsActive = true,
                DefaultBranchId = req.PrimaryBranchId > 0 ? req.PrimaryBranchId
                                : req.BranchIds?.FirstOrDefault() > 0 ? req.BranchIds.First() : null
            };

            var result = await _userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return Json(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });

            await _userManager.AddToRoleAsync(user, req.Role);

            if (req.BranchIds != null && req.BranchIds.Any())
            {
                var primaryId = req.PrimaryBranchId ?? req.BranchIds.First();
                foreach (var bid in req.BranchIds)
                {
                    _context.UserBranches.Add(new UserBranch
                    {
                        UserId = user.Id,
                        BranchId = bid,
                        IsPrimary = bid == primaryId
                    });
                }
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true, userId = user.Id });
        }

        [HttpPost]
        public async Task<IActionResult> EditUser([FromBody] EditUserRequest req)
        {
            if (req == null)
                return Json(new { success = false, message = "بيانات غير صحيحة" });

            var user = await _userManager.FindByIdAsync(req.UserId) as ApplicationUser;
            if (user == null) return Json(new { success = false, message = "المستخدم غير موجود" });

            user.FullName = req.FullName;
            user.FullNameAr = req.FullNameAr;
            if (!string.IsNullOrEmpty(req.Email) && req.Email != user.Email)
            {
                user.Email = req.Email;
                user.UserName = req.Email;
            }
            await _userManager.UpdateAsync(user);

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (!string.IsNullOrEmpty(req.Role) && !currentRoles.Contains(req.Role))
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, req.Role);
            }

            return Json(new { success = true });
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteUser([FromBody] DeleteByIdStringRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.Id))
                return Json(new { success = false, message = "معرف المستخدم مطلوب" });

            var user = await _userManager.FindByIdAsync(req.Id);
            if (user == null) return Json(new { success = false, message = "المستخدم غير موجود" });

            // Prevent deleting yourself
            var currentUserId = _userManager.GetUserId(User);
            if (user.Id == currentUserId)
                return Json(new { success = false, message = "لا يمكنك حذف حسابك الخاص" });

            // Remove user-branch assignments first
            var userBranches = await _context.UserBranches.Where(ub => ub.UserId == user.Id).ToListAsync();
            _context.UserBranches.RemoveRange(userBranches);
            await _context.SaveChangesAsync();

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
                return Json(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleUser([FromBody] ToggleUserRequest req)   // ← FIX: was string id
        {
            if (req == null || string.IsNullOrEmpty(req.UserId))
                return Json(new { success = false });

            var user = await _userManager.FindByIdAsync(req.UserId);
            if (user == null) return Json(new { success = false });

            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);
            return Json(new { success = true, isActive = user.IsActive });
        }

        // ← FIX: frontend calls /Admin/ResetUserPassword not /Admin/ResetPassword
        [HttpPost]
        public async Task<IActionResult> ResetUserPassword([FromBody] ResetPasswordRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.UserId))
                return Json(new { success = false, message = "بيانات غير صحيحة" });

            var user = await _userManager.FindByIdAsync(req.UserId);
            if (user == null) return Json(new { success = false, message = "المستخدم غير موجود" });

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, req.NewPassword);
            return Json(new { success = result.Succeeded, message = result.Succeeded ? "تم إعادة تعيين كلمة المرور" : string.Join(", ", result.Errors.Select(e => e.Description)) });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateUserBranches([FromBody] UpdateUserBranchesRequest req)
        {
            if (req == null) return Json(new { success = false });

            var existing = await _context.UserBranches
                .Where(ub => ub.UserId == req.UserId).ToListAsync();
            _context.UserBranches.RemoveRange(existing);

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

            var user = await _userManager.FindByIdAsync(req.UserId) as ApplicationUser;
            if (user != null)
            {
                user.DefaultBranchId = req.BranchIds.Contains(primaryId)
                    ? primaryId : req.BranchIds.FirstOrDefault();
                await _userManager.UpdateAsync(user);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ═════════════════════════════════════════════════════
        // ORDERS
        // ═════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════
        // TABLES
        // ═════════════════════════════════════════════════════
        public async Task<IActionResult> Tables()
        {
            var tables = await _context.DiningTables
                .Include(t => t.Orders.Where(o =>
                    o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing))
                .ToListAsync();
            return View(tables);
        }

        [HttpPost]
        public async Task<IActionResult> SaveTable([FromBody] DiningTable model)
        {
            if (model == null) return Json(new { success = false });
            if (model.Id == 0)
                _context.DiningTables.Add(model);
            else
                _context.Update(model);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTableStatus([FromBody] UpdateTableStatusRequest req)
        {
            var table = await _context.DiningTables.FindAsync(req.TableId);
            if (table == null) return Json(new { success = false, message = "الطاولة غير موجودة" });
            if (Enum.TryParse<TableStatus>(req.Status, out var status))
            {
                table.Status = status;
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "حالة غير صحيحة" });
        }

        // ═════════════════════════════════════════════════════
        // EXPENSES
        // ═════════════════════════════════════════════════════
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
        public async Task<IActionResult> SaveExpense([FromBody] SaveExpenseDto dto)
        {
            if (dto == null)
                return Json(new { success = false, message = "بيانات غير صحيحة" });

            if (string.IsNullOrWhiteSpace(dto.Title))
                return Json(new { success = false, message = "يرجى إدخال عنوان المصروف" });

            if (dto.Amount <= 0)
                return Json(new { success = false, message = "يرجى إدخال مبلغ صحيح" });

            var branchId = await GetBranchIdAsync();

            var userId = _userManager.GetUserId(User);

            if (dto.Id == 0)
            {
                var expense = new Expense
                {
                    Title = dto.Title,
                    Description = dto.Description,
                    Amount = dto.Amount,
                    Category = dto.Category ?? "",
                    PaymentMethod = dto.PaymentMethod,
                    Notes = dto.Notes,
                    Date = dto.Date == default ? DateTime.Now : dto.Date,
                    BranchId = branchId,
                    CreatedById = userId,
                    RecordedById = userId
                };
                _context.Expenses.Add(expense);
            }
            else
            {
                var expense = await _context.Expenses
                    .FirstOrDefaultAsync(e => e.Id == dto.Id);

                if (expense == null)
                    return Json(new { success = false, message = "المصروف غير موجود" });

                expense.Title = dto.Title;
                expense.Description = dto.Description;
                expense.Amount = dto.Amount;
                expense.Category = dto.Category ?? "";
                expense.PaymentMethod = dto.PaymentMethod;
                expense.Notes = dto.Notes;
                expense.Date = dto.Date == default ? expense.Date : dto.Date;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteExpense([FromBody] DeleteByIdRequest req)  // ← FIX: was (int id)
        {
            var expense = await _context.Expenses.FindAsync(req.Id);
            if (expense == null) return Json(new { success = false });
            _context.Expenses.Remove(expense);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ═════════════════════════════════════════════════════
        // INVENTORY
        // ═════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════
        // SETTINGS
        // ═════════════════════════════════════════════════════
        public async Task<IActionResult> Settings()
        {
            var settings = await _context.SystemSettings.ToListAsync();
            return View(settings.ToDictionary(s => s.Key, s => s.Value));
        }

        [HttpPost]
        public async Task<IActionResult> SaveSettings([FromBody] Dictionary<string, string> settings)
        {
            if (settings == null) return Json(new { success = false });
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

        // ═════════════════════════════════════════════════════
        // SHIFTS
        // ═════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════
        // REPORTS
        // ═════════════════════════════════════════════════════
        public async Task<IActionResult> Reports()
        {
            ViewBag.Branches = await _context.Branches
                .Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetReportData(int days = 30, int? branchId = null)
        {
            var from = DateTime.Today.AddDays(-days);
            var previousFrom = from.AddDays(-days);

            var completedStatuses = new[] {
                OrderStatus.Completed, OrderStatus.Refunded, OrderStatus.PartialRefund
            };

            IQueryable<Order> OQ() { var q = _context.Orders.AsQueryable(); if (branchId.HasValue) q = q.Where(o => o.BranchId == branchId); return q; }
            IQueryable<Refund> RQ() { var q = _context.Refunds.AsQueryable(); if (branchId.HasValue) q = q.Where(r => r.BranchId == branchId); return q; }
            IQueryable<Expense> EQ() { var q = _context.Expenses.AsQueryable(); if (branchId.HasValue) q = q.Where(e => e.BranchId == branchId); return q; }
            IQueryable<OrderItem> OIQ() { var q = _context.OrderItems.AsQueryable(); if (branchId.HasValue) q = q.Where(oi => oi.Order!.BranchId == branchId); return q; }

            var completedOrders = await OQ().Where(o => completedStatuses.Contains(o.Status) && o.CreatedAt.Date >= from).ToListAsync();
            var previousOrders = await OQ().Where(o => completedStatuses.Contains(o.Status) && o.CreatedAt.Date >= previousFrom && o.CreatedAt.Date < from).ToListAsync();
            var periodRefunds = await RQ().Where(r => r.CreatedAt.Date >= from && r.Status == RefundStatus.Completed).ToListAsync();
            var prevPeriodRefunds = await RQ().Where(r => r.CreatedAt.Date >= previousFrom && r.CreatedAt.Date < from && r.Status == RefundStatus.Completed).ToListAsync();
            var periodExpenses = await EQ().Where(e => e.Date >= from).ToListAsync();

            var expenseByDay = periodExpenses.GroupBy(e => e.Date.Date).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
            var totalExpenses = periodExpenses.Sum(e => e.Amount);
            var grossRevenue = completedOrders.Sum(o => o.Total);
            var refundedAmount = periodRefunds.Sum(r => r.RefundTotal);
            var totalRevenue = Math.Max(0, grossRevenue - refundedAmount);
            var prevRevenue = Math.Max(0, previousOrders.Sum(o => o.Total) - prevPeriodRefunds.Sum(r => r.RefundTotal));
            var revenueGrowth = prevRevenue > 0 ? Math.Round((totalRevenue - prevRevenue) / prevRevenue * 100, 1) : 0m;
            var ordersGrowth = previousOrders.Count > 0
                ? Math.Round((decimal)(completedOrders.Count - previousOrders.Count) / previousOrders.Count * 100, 1) : 0m;

            var totalCogs = await OIQ()
                .Include(oi => oi.Product)
                .Where(oi => completedStatuses.Contains(oi.Order!.Status) && oi.Order.CreatedAt.Date >= from && oi.Product != null)
                .SumAsync(oi => oi.Product!.CostPrice * oi.Quantity);

            var grossProfit = totalRevenue - totalCogs;
            var netProfit = grossProfit - totalExpenses;
            var profitMargin = totalRevenue > 0 ? Math.Round(netProfit / totalRevenue * 100, 1) : 0m;

            var refundByDay = periodRefunds.GroupBy(r => r.CreatedAt.Date).ToDictionary(g => g.Key, g => g.Sum(r => r.RefundTotal));
            var allDates = Enumerable.Range(0, days).Select(i => from.AddDays(i)).ToList();
            var ordersByDay = completedOrders.GroupBy(o => o.CreatedAt.Date).ToDictionary(g => g.Key, g => g.ToList());

            var dailySales = allDates.Select(date =>
            {
                var orders = ordersByDay.GetValueOrDefault(date, new());
                var gross = orders.Sum(o => o.Total);
                var refunds = refundByDay.GetValueOrDefault(date, 0);
                var expenses = expenseByDay.GetValueOrDefault(date, 0);
                var rev = Math.Max(0, gross - refunds);
                return new
                {
                    date = date.ToString("MM/dd"),
                    fullDate = date.ToString("yyyy-MM-dd"),
                    dayName = date.ToString("ddd"),
                    dayNameAr = GetArabicDayName(date),
                    revenue = rev,
                    orders = orders.Count,
                    refunds,
                    expenses,
                    profit = rev - expenses
                };
            }).ToList();

            var weeklySales = allDates
                .GroupBy(d => System.Globalization.CultureInfo.CurrentCulture.Calendar
                    .GetWeekOfYear(d, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Monday))
                .Select(wg =>
                {
                    var wDates = wg.ToList();
                    var wOrders = wDates.SelectMany(d => ordersByDay.GetValueOrDefault(d, new())).ToList();
                    var wGross = wOrders.Sum(o => o.Total);
                    var wRef = wDates.Sum(d => refundByDay.GetValueOrDefault(d, 0));
                    var wExp = wDates.Sum(d => expenseByDay.GetValueOrDefault(d, 0));
                    var wRev = Math.Max(0, wGross - wRef);
                    return new { week = $"W{wg.Key} ({wDates.First():MM/dd}–{wDates.Last():MM/dd})", revenue = wRev, orders = wOrders.Count, expenses = wExp, profit = wRev - wExp };
                }).ToList();

            var monthlySales = allDates
                .GroupBy(d => new { d.Year, d.Month })
                .Select(mg =>
                {
                    var mDates = mg.ToList();
                    var mOrders = mDates.SelectMany(d => ordersByDay.GetValueOrDefault(d, new())).ToList();
                    var mGross = mOrders.Sum(o => o.Total);
                    var mRef = mDates.Sum(d => refundByDay.GetValueOrDefault(d, 0));
                    var mExp = mDates.Sum(d => expenseByDay.GetValueOrDefault(d, 0));
                    var mRev = Math.Max(0, mGross - mRef);
                    return new { month = new DateTime(mg.Key.Year, mg.Key.Month, 1).ToString("MMMM yyyy"), revenue = mRev, orders = mOrders.Count, expenses = mExp, profit = mRev - mExp };
                }).ToList();

            var topProducts = await OIQ()
                .Include(oi => oi.Product)
                .Where(oi => completedStatuses.Contains(oi.Order!.Status) && oi.Order.CreatedAt.Date >= from)
                .GroupBy(oi => new { oi.ProductId, oi.ProductName, oi.ProductNameAr })
                .Select(g => new { productId = g.Key.ProductId, productName = g.Key.ProductName, productNameAr = g.Key.ProductNameAr, totalQty = g.Sum(oi => oi.Quantity), totalRevenue = g.Sum(oi => oi.TotalPrice) })
                .OrderByDescending(x => x.totalRevenue).Take(15).ToListAsync();

            var topWithCost = new List<object>();
            foreach (var p in topProducts)
            {
                var product = await _context.Products.FindAsync(p.productId);
                var cost = (product?.CostPrice ?? 0) * p.totalQty;
                var margin = p.totalRevenue > 0 ? Math.Round((p.totalRevenue - cost) / p.totalRevenue * 100, 1) : 0;
                topWithCost.Add(new { p.productName, p.productNameAr, p.totalQty, p.totalRevenue, cost, profit = p.totalRevenue - cost, margin });
            }

            var categoryRevenue = await OIQ()
                .Include(oi => oi.Product).ThenInclude(p => p!.Category)
                .Where(oi => completedStatuses.Contains(oi.Order!.Status) && oi.Order.CreatedAt.Date >= from && oi.Product!.Category != null)
                .GroupBy(oi => new { oi.Product!.Category!.Name, oi.Product.Category.NameAr, oi.Product.Category.ColorHex })
                .Select(g => new { categoryName = g.Key.Name, categoryNameAr = g.Key.NameAr, colorHex = g.Key.ColorHex, revenue = g.Sum(oi => oi.TotalPrice), qty = g.Sum(oi => oi.Quantity) })
                .OrderByDescending(x => x.revenue).ToListAsync();

            var paymentMethods = completedOrders.GroupBy(o => o.PaymentMethod.ToString())
                .ToDictionary(g => g.Key, g => new { count = g.Count(), revenue = g.Sum(o => o.Total) });
            var orderTypes = completedOrders.GroupBy(o => o.OrderType.ToString())
                .ToDictionary(g => g.Key, g => new { count = g.Count(), revenue = g.Sum(o => o.Total) });

            List<object> branchBreakdown = new();
            if (!branchId.HasValue)
            {
                var branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
                foreach (var b in branches)
                {
                    var bRev = await _context.Orders
                        .Where(o => o.BranchId == b.Id && completedStatuses.Contains(o.Status) && o.CreatedAt.Date >= from)
                        .SumAsync(o => o.Total);
                    var bRef = await _context.Refunds
                        .Where(r => r.BranchId == b.Id && r.Status == RefundStatus.Completed && r.CreatedAt.Date >= from)
                        .SumAsync(r => r.RefundTotal);
                    var bExp = await _context.Expenses
                        .Where(e => e.BranchId == b.Id && e.Date >= from).SumAsync(e => e.Amount);
                    var bNetRev = Math.Max(0, bRev - bRef);
                    var bOrders = await _context.Orders.CountAsync(o => o.BranchId == b.Id && completedStatuses.Contains(o.Status) && o.CreatedAt.Date >= from);
                    branchBreakdown.Add(new
                    {
                        branchId = b.Id,
                        branchName = b.Name,
                        branchNameAr = b.NameAr,
                        icon = b.Icon,
                        colorHex = b.ColorHex,
                        revenue = bNetRev,
                        expenses = bExp,
                        profit = bNetRev - bExp,
                        orders = bOrders
                    });
                }
            }

            var avgOrderValue = completedOrders.Count > 0 ? totalRevenue / completedOrders.Count : 0;
            var uniqueCustomers = completedOrders.Where(o => !string.IsNullOrEmpty(o.CustomerName)).Select(o => o.CustomerName).Distinct().Count();
            var totalRefundCount = periodRefunds.Count;

            return Json(new
            {
                totalRevenue,
                totalOrders = completedOrders.Count,
                avgOrderValue,
                revenueGrowth,
                ordersGrowth,
                uniqueCustomers,
                totalExpenses,
                totalCogs,
                grossProfit,
                netProfit,
                profitMargin,
                totalRefunded = refundedAmount,
                totalRefundCount,
                periodDays = days,
                dailySales,
                weeklySales,
                monthlySales,
                topProducts = topWithCost,
                categoryRevenue,
                paymentMethods,
                orderTypes,
                branchBreakdown,
                generatedAt = DateTime.Now
            });
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
            var shifts = await _context.Shifts
                .Include(s => s.User)
                .Where(s => !s.IsClosed)
                .Select(s => new
                {
                    s.Id,
                    s.TotalSales,
                    cashierName = s.User != null ? s.User.UserName : null,
                    startTime = s.StartTime.ToString("hh:mm tt")
                })
                .ToListAsync();
            return Json(new { shifts });
        }

        // ═════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════
        private static string GetArabicDayName(DateTime date) => date.DayOfWeek switch
        {
            DayOfWeek.Saturday => "السبت",
            DayOfWeek.Sunday => "الأحد",
            DayOfWeek.Monday => "الاثنين",
            DayOfWeek.Tuesday => "الثلاثاء",
            DayOfWeek.Wednesday => "الأربعاء",
            DayOfWeek.Thursday => "الخميس",
            DayOfWeek.Friday => "الجمعة",
            _ => ""
        };

        // ═════════════════════════════════════════════════════
        // REQUEST / DTO MODELS
        // ═════════════════════════════════════════════════════
        public class SaveExpenseDto
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public string? Description { get; set; }
            public decimal Amount { get; set; }
            public string Category { get; set; } = "";
            public string? PaymentMethod { get; set; }
            public string? Notes { get; set; }
            public DateTime Date { get; set; } = DateTime.Now;
        }

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

        public class EditUserRequest
        {
            public string UserId { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string FullNameAr { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
        }

        public class ToggleUserRequest                          // ← NEW
        {
            public string UserId { get; set; } = string.Empty;
        }

        public class ResetPasswordRequest
        {
            public string UserId { get; set; } = string.Empty;
            public string NewPassword { get; set; } = string.Empty;
        }

        public class UpdateUserBranchesRequest
        {
            public string UserId { get; set; } = string.Empty;
            public List<int> BranchIds { get; set; } = new();
            public int? PrimaryBranchId { get; set; }
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

        public class DeleteByIdRequest                         // for DeleteExpense
        {
            public int Id { get; set; }
        }

        public class DeleteByIdStringRequest                   // for DeleteUser
        {
            public string Id { get; set; } = string.Empty;
        }
    }
}
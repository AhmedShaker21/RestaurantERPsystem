using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantERP.Data;
using RestaurantERP.Helpers;
using RestaurantERP.Models;
using RestaurantERP.Services;

namespace RestaurantERP.Controllers
{
    [Authorize(Roles = "Admin,Manager,Cashier")]
    public class CashierController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly OrderService _orderService;
        private readonly BranchService _branchService;

        public CashierController(ApplicationDbContext context,
                                  UserManager<ApplicationUser> userManager,
                                  OrderService orderService,
                                  BranchService branchService)
        {
            _context = context;
            _userManager = userManager;
            _orderService = orderService;
            _branchService = branchService;
        }

        public async Task<IActionResult> Index()
        {
            var branchId = await _branchService.GetCurrentBranchIdAsync();
            var branch = await _context.Branches.FindAsync(branchId);

            var categories = await _context.Categories
                .Where(c => c.IsActive)
                .Include(c => c.Products.Where(p => p.IsActive && p.IsAvailable))
                .ToListAsync();

            var tables = await _context.DiningTables
                .Where(t => t.BranchId == branchId)
                .ToListAsync();

            var settings = await _context.SystemSettings
                .Where(s => s.BranchId == branchId || s.BranchId == null)
                .ToListAsync();

            // Branch-specific settings override global ones
            var settingsDict = settings
                .OrderBy(s => s.BranchId == null ? 0 : 1)
                .GroupBy(s => s.Key)
                .ToDictionary(g => g.Key, g => g.Last().Value);

            ViewBag.Settings = settingsDict;
            ViewBag.Tables = tables;
            ViewBag.Branch = branch;
            ViewBag.BranchId = branchId;
            return View(categories);
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts(int? categoryId)
        {
            var query = _context.Products
                .Include(p => p.Category)
                .Where(p => p.IsActive && p.IsAvailable);
            if (categoryId.HasValue)
                query = query.Where(p => p.CategoryId == categoryId);
            var products = await query.Select(p => new
            {
                p.Id,
                p.Name,
                p.NameAr,
                p.Price,
                p.ImageUrl,
                p.StockQuantity,
                p.TrackStock,
                p.Barcode,
                CategoryName = p.Category!.Name,
                CategoryNameAr = p.Category.NameAr,
                CategoryColor = p.Category.ColorHex,
                CategoryIcon = p.Category.Icon,
                SkipKitchen = p.Category.SkipKitchen
            }).ToListAsync();
            return Json(products);
        }

        [HttpPost]
        public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest req)
        {
            var userId = _userManager.GetUserId(User);
            var branchId = await _branchService.GetCurrentBranchIdAsync();

            // ── SHIFT CHECK: reject if no open shift ──────────────
            var openShift = await _context.Shifts
                .FirstOrDefaultAsync(s => s.UserId == userId && s.BranchId == branchId && !s.IsClosed);

            if (openShift == null)
                return Json(new { success = false, message = "لا توجد وردية مفتوحة — يرجى فتح وردية أولاً قبل إنشاء الطلبات" });

            var settings = await _context.SystemSettings
                .Where(s => s.BranchId == branchId || s.BranchId == null)
                .ToListAsync();
            var taxRate = decimal.Parse(
                settings.Where(s => s.Key == "TaxRate")
                        .OrderByDescending(s => s.BranchId.HasValue)
                        .FirstOrDefault()?.Value ?? "14");

            var items = new List<OrderItem>();
            foreach (var item in req.Items)
            {
                var product = await _context.Products
                    .Include(p => p.Category)
                    .FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product == null) continue;

                items.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    ProductName = product.Name,
                    ProductNameAr = product.NameAr,
                    Quantity = item.Quantity,
                    UnitPrice = product.Price,
                    TotalPrice = product.Price * item.Quantity,
                    Notes = item.Notes,
                    // Inherit skip-kitchen flag from category
                    SkipKitchen = product.Category?.SkipKitchen ?? false
                });
                if (product.TrackStock)
                {
                    product.StockQuantity -= item.Quantity;
                    _context.Update(product);
                }
            }

            var order = new Order
            {
                CashierId = userId,
                TableId = req.TableId,
                BranchId = branchId,
                OrderType = req.Type,
                CustomerName = req.CustomerName,
                CustomerPhone = req.CustomerPhone,
                Notes = req.Notes,
                TaxRate = taxRate,
                DiscountAmount = req.DiscountAmount,
                AmountPaid = req.AmountPaid,
                PaymentMethod = req.PaymentMethod,
                Status = OrderStatus.Pending
            };

            var created = await _orderService.CreateOrderAsync(order, items);

            // ── Increment shift order count ───────────────────────
            openShift.TotalOrders++;
            await _context.SaveChangesAsync();

            return Json(new { success = true, orderId = created.Id, orderNumber = created.OrderNumber, total = created.Total });
        }

        [HttpGet]
        public async Task<IActionResult> GetOrderForPrint(int? id, int? orderId)
        {
            var resolvedId = id ?? orderId ?? 0;
            var order = await _context.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Table)
                .Include(o => o.Cashier)
                .Include(o => o.Branch)
                .FirstOrDefaultAsync(o => o.Id == resolvedId);

            if (order == null)
                return Json(new { success = false, message = "Order not found" });

            order.IsPrinted = true;
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                id = order.Id,
                orderNumber = order.OrderNumber,
                createdAt = order.CreatedAt,
                type = (int)order.OrderType,
                orderType = order.OrderType.ToString(),
                status = order.Status.ToString(),
                paymentMethod = (int)order.PaymentMethod,
                subTotal = order.SubTotal,
                taxRate = order.TaxRate,
                taxAmount = order.TaxAmount,
                discountAmount = order.DiscountAmount,
                total = order.Total,
                amountPaid = order.AmountPaid,
                change = order.Change,
                customerName = order.CustomerName,
                notes = order.Notes,
                table = order.Table?.TableNumber,
                cashier = (order.Cashier as ApplicationUser)?.FullName
                                 ?? (order.Cashier as ApplicationUser)?.FullNameAr
                                 ?? order.Cashier?.UserName ?? order.Cashier?.Email ?? "—",
                cashierAr = (order.Cashier as ApplicationUser)?.FullNameAr
                                 ?? (order.Cashier as ApplicationUser)?.FullName
                                 ?? order.Cashier?.UserName ?? "—",
                branchName = order.Branch?.Name,
                branchNameAr = order.Branch?.NameAr,
                items = order.Items.Select(i => new
                {
                    productId = i.ProductId,
                    productName = !string.IsNullOrEmpty(i.ProductName) ? i.ProductName : i.Product?.Name ?? "Item",
                    productNameAr = !string.IsNullOrEmpty(i.ProductNameAr) ? i.ProductNameAr : i.Product?.NameAr ?? "",
                    quantity = i.Quantity,
                    unitPrice = i.UnitPrice,
                    totalPrice = i.TotalPrice,
                    notes = i.Notes
                })
            });
        }

        public async Task<IActionResult> History(DateTime? date)
        {
            date ??= DateTime.Today;
            var userId = _userManager.GetUserId(User);
            var branchId = await _branchService.GetCurrentBranchIdAsync();
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("Manager");

            var query = _context.Orders
                .Include(o => o.Items)
                .Include(o => o.Table)
                .Where(o => o.CreatedAt.Date == date.Value.Date && o.BranchId == branchId);

            if (!isAdmin)
                query = query.Where(o => o.CashierId == userId);

            var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
            ViewBag.Date = date.Value.ToString("yyyy-MM-dd");

            var completedStatuses = new[] { OrderStatus.Completed, OrderStatus.Refunded, OrderStatus.PartialRefund };
            var grossSales = orders.Where(o => completedStatuses.Contains(o.Status)).Sum(o => o.Total);
            var refundedToday = await _context.Refunds
                .Where(r => r.BranchId == branchId && r.CreatedAt.Date == date.Value.Date
                         && r.Status == RefundStatus.Completed
                         && (!isAdmin ? r.ProcessedById == userId : true))
                .SumAsync(r => r.RefundTotal);

            ViewBag.TotalSales = Math.Max(0, grossSales - refundedToday);
            return View(orders);
        }

        [HttpPost]
        public async Task<IActionResult> OpenShift([FromBody] OpenShiftRequest req)
        {
            var userId = _userManager.GetUserId(User);
            var branchId = await _branchService.GetCurrentBranchIdAsync();
            var existing = await _context.Shifts
                .FirstOrDefaultAsync(s => s.UserId == userId && s.BranchId == branchId && !s.IsClosed);
            if (existing != null) return Json(new { success = false, message = "Shift already open" });

            _context.Shifts.Add(new Shift
            {
                UserId = userId!,
                BranchId = branchId,
                OpeningCash = req.OpeningCash,
                StartTime = DateTime.Now
            });
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> CloseShift([FromBody] CloseShiftRequest req)
        {
            var userId = _userManager.GetUserId(User);
            var branchId = await _branchService.GetCurrentBranchIdAsync();
            var shift = await _context.Shifts
                .FirstOrDefaultAsync(s => s.UserId == userId && s.BranchId == branchId && !s.IsClosed);
            if (shift == null) return Json(new { success = false });

            var statuses = new[] { OrderStatus.Completed, OrderStatus.Refunded, OrderStatus.PartialRefund };
            var gross = await _context.Orders
                .Where(o => o.CashierId == userId && o.BranchId == branchId
                         && o.CreatedAt >= shift.StartTime && statuses.Contains(o.Status)
                         && o.PaymentMethod == PaymentMethod.Cash)
                .SumAsync(o => o.Total);
            var refunds = await _context.Refunds
                .Where(r => r.ProcessedById == userId && r.BranchId == branchId
                         && r.CreatedAt >= shift.StartTime && r.Status == RefundStatus.Completed
                         && r.RefundMethod == RefundMethod.Cash)
                .SumAsync(r => r.RefundTotal);

            shift.EndTime = DateTime.Now;
            shift.ClosingCash = req.ClosingCash;
            shift.TotalSales = Math.Max(0, gross - refunds);
            shift.IsClosed = true;
            shift.Notes = req.Notes;
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                totalSales = shift.TotalSales,
                expectedCash = shift.OpeningCash + shift.TotalSales,
                difference = req.ClosingCash - (shift.OpeningCash + shift.TotalSales)
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetCurrentShift()
        {
            var userId = _userManager.GetUserId(User);
            var branchId = await _branchService.GetCurrentBranchIdAsync();
            var shift = await _context.Shifts
                .FirstOrDefaultAsync(s => s.UserId == userId && s.BranchId == branchId && !s.IsClosed);
            return Json(shift != null
                ? new { isOpen = true, shift.Id, shift.StartTime, shift.OpeningCash }
                : new { isOpen = false });
        }

        [HttpPost]
        public async Task<IActionResult> CancelOrder(int id, string reason)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null) return Json(new { success = false });
            if (order.Status == OrderStatus.Completed)
                return Json(new { success = false, message = "Cannot cancel completed order" });
            order.Status = OrderStatus.Cancelled;
            order.Notes = $"CANCELLED: {reason}";
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
        [HttpPost]
        public IActionResult OpenDrawer()
        {
            CashDrawer.OpenDrawer();
            return Ok();
        }
    }

    public class PlaceOrderRequest
    {
        public List<OrderItemRequest> Items { get; set; } = new();
        public int? TableId { get; set; }
        public OrderType Type { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }
        public string? Notes { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal AmountPaid { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
    }
    public class OrderItemRequest { public int ProductId { get; set; } public int Quantity { get; set; } public string? Notes { get; set; } }
    public class OpenShiftRequest { public decimal OpeningCash { get; set; } }
    public class CloseShiftRequest { public decimal ClosingCash { get; set; } public string? Notes { get; set; } }
}
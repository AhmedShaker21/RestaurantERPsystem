using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantERP.Data;
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

        public CashierController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            OrderService orderService)
        {
            _context = context;
            _userManager = userManager;
            _orderService = orderService;
        }

        public async Task<IActionResult> Index()
        {
            var categories = await _context.Categories
                .Where(c => c.IsActive)
                .Include(c => c.Products.Where(p => p.IsActive && p.IsAvailable))
                .ToListAsync();

            var tables = await _context.DiningTables.ToListAsync();
            var settings = await _context.SystemSettings.ToListAsync();

            ViewBag.Settings = settings.ToDictionary(s => s.Key, s => s.Value);
            ViewBag.Tables = tables;

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
                categoryId = p.CategoryId,
                CategoryName = p.Category!.Name,
                CategoryNameAr = p.Category.NameAr,
                CategoryColor = p.Category.ColorHex,
                CategoryIcon = p.Category.Icon
            }).ToListAsync();

            return Json(products);
        }

        [HttpPost]
        public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest req)
        {
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(userId))
            {
                return Json(new
                {
                    success = false,
                    message = "User not found"
                });
            }

            var currentShift = await _context.Shifts
                .FirstOrDefaultAsync(s => s.UserId == userId && !s.IsClosed);

            if (currentShift == null)
            {
                return Json(new
                {
                    success = false,
                    message = "لازم تفتح وردية الأول"
                });
            }

            var settings = await _context.SystemSettings.ToListAsync();
            var taxRate = decimal.Parse(settings.FirstOrDefault(s => s.Key == "TaxRate")?.Value ?? "14");

            var items = new List<OrderItem>();

            foreach (var item in req.Items)
            {
                var product = await _context.Products.FindAsync(item.ProductId);

                if (product == null)
                    continue;

                if (product.TrackStock && product.StockQuantity < item.Quantity)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"الكمية غير كافية للمنتج: {product.NameAr ?? product.Name}"
                    });
                }

                items.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = product.Price,
                    TotalPrice = product.Price * item.Quantity,
                    Notes = item.Notes
                });

                if (product.TrackStock)
                {
                    product.StockQuantity -= item.Quantity;
                    _context.Update(product);
                }
            }

            if (!items.Any())
            {
                return Json(new
                {
                    success = false,
                    message = "لا يوجد منتجات في الطلب"
                });
            }

            var order = new Order
            {
                CashierId = userId,
                ShiftId = currentShift.Id,
                TableId = req.TableId,
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

            return Json(new
            {
                success = true,
                orderId = created.Id,
                orderNumber = created.OrderNumber,
                total = created.Total
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetOrderForPrint(int? id, int? orderId)
        {
            var resolvedId = id ?? orderId ?? 0;

            var order = await _context.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Table)
                .Include(o => o.Cashier)
                .FirstOrDefaultAsync(o => o.Id == resolvedId);

            if (order == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Order not found"
                });
            }

            order.IsPrinted = true;
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                order = new
                {
                    order.Id,
                    order.OrderNumber,
                    order.CreatedAt,
                    orderType = order.OrderType.ToString(),
                    status = order.Status.ToString(),
                    paymentMethod = order.PaymentMethod.ToString(),
                    order.SubTotal,
                    order.TaxRate,
                    order.TaxAmount,
                    order.DiscountAmount,
                    order.Total,
                    order.AmountPaid,
                    order.Change,
                    order.CustomerName,
                    order.CustomerPhone,
                    order.Notes,
                    tableNumber = order.Table?.TableNumber,
                    cashierName = order.Cashier?.UserName,
                    items = order.Items.Select(i => new
                    {
                        i.ProductId,
                        productName = i.Product != null ? i.Product.Name : "Unknown",
                        productNameAr = i.Product != null ? i.Product.NameAr : "غير معروف",
                        i.Quantity,
                        i.UnitPrice,
                        i.TotalPrice,
                        i.Notes
                    })
                }
            });
        }

        public async Task<IActionResult> History(DateTime? date)
        {
            date ??= DateTime.Today;

            var userId = _userManager.GetUserId(User);
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("Manager");

            var query = _context.Orders
                .Include(o => o.Items)
                .Include(o => o.Table)
                .Where(o => o.CreatedAt.Date == date.Value.Date);

            if (!isAdmin)
                query = query.Where(o => o.CashierId == userId);

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            ViewBag.Date = date.Value.ToString("yyyy-MM-dd");
            ViewBag.TotalSales = orders
                .Where(o => o.Status == OrderStatus.Completed)
                .Sum(o => o.Total);

            return View(orders);
        }

        // ===== SHIFT MANAGEMENT =====

        [HttpPost]
        public async Task<IActionResult> OpenShift([FromBody] OpenShiftRequest req)
        {
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(userId))
            {
                return Json(new
                {
                    success = false,
                    message = "User not found"
                });
            }

            var existing = await _context.Shifts
                .FirstOrDefaultAsync(s => s.UserId == userId && !s.IsClosed);

            if (existing != null)
            {
                return Json(new
                {
                    success = false,
                    message = "Shift already open"
                });
            }

            var shift = new Shift
            {
                UserId = userId,
                OpeningCash = req.OpeningCash,
                StartTime = DateTime.Now,
                IsClosed = false,
                TotalSales = 0
            };

            _context.Shifts.Add(shift);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                shiftId = shift.Id
            });
        }

        [HttpPost]
        public async Task<IActionResult> CloseShift([FromBody] CloseShiftRequest req)
        {
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(userId))
            {
                return Json(new
                {
                    success = false,
                    message = "User not found"
                });
            }

            var shift = await _context.Shifts
                .FirstOrDefaultAsync(s => s.UserId == userId && !s.IsClosed);

            if (shift == null)
            {
                return Json(new
                {
                    success = false,
                    message = "لا توجد وردية مفتوحة"
                });
            }

            var shiftOrders = await _context.Orders
                .Where(o =>
                    o.ShiftId == shift.Id &&
                    o.Status == OrderStatus.Completed)
                .ToListAsync();

            var totalSales = shiftOrders.Sum(o => o.Total);

            var cashSales = shiftOrders
                .Where(o => o.PaymentMethod == PaymentMethod.Cash)
                .Sum(o => o.Total);

            shift.EndTime = DateTime.Now;
            shift.ClosingCash = req.ClosingCash;
            shift.TotalSales = totalSales;
            shift.IsClosed = true;
            shift.Notes = req.Notes;

            await _context.SaveChangesAsync();

            var expectedCash = shift.OpeningCash + cashSales;
            var difference = req.ClosingCash - expectedCash;

            return Json(new
            {
                success = true,
                totalSales,
                cashSales,
                expectedCash,
                difference
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetCurrentShift()
        {
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(userId))
            {
                return Json(new
                {
                    isOpen = false
                });
            }

            var shift = await _context.Shifts
                .FirstOrDefaultAsync(s => s.UserId == userId && !s.IsClosed);

            if (shift == null)
            {
                return Json(new
                {
                    isOpen = false
                });
            }

            var shiftOrders = await _context.Orders
                .Where(o => o.ShiftId == shift.Id)
                .ToListAsync();

            var completedOrders = shiftOrders
                .Where(o => o.Status == OrderStatus.Completed)
                .ToList();

            var totalSales = completedOrders.Sum(o => o.Total);
            var ordersCount = shiftOrders.Count;
            var completedOrdersCount = completedOrders.Count;

            return Json(new
            {
                isOpen = true,
                shift.Id,
                shift.StartTime,
                shift.OpeningCash,
                totalSales,
                ordersCount,
                completedOrdersCount
            });
        }

        [HttpPost]
        public async Task<IActionResult> CancelOrder(int id, string reason)
        {
            var order = await _context.Orders.FindAsync(id);

            if (order == null)
                return Json(new { success = false });

            if (order.Status == OrderStatus.Completed)
            {
                return Json(new
                {
                    success = false,
                    message = "Cannot cancel completed order"
                });
            }

            order.Status = OrderStatus.Cancelled;
            order.Notes = $"CANCELLED: {reason}";

            await _context.SaveChangesAsync();

            return Json(new { success = true });
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

    public class OrderItemRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public string? Notes { get; set; }
    }

    public class OpenShiftRequest
    {
        public decimal OpeningCash { get; set; }
    }

    public class CloseShiftRequest
    {
        public decimal ClosingCash { get; set; }
        public string? Notes { get; set; }
    }
}
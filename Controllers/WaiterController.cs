using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantERP.Data;
using RestaurantERP.Models;
using RestaurantERP.Services;

namespace RestaurantERP.Controllers
{
    [Authorize(Roles = "Admin,Manager,Waiter")]
    public class WaiterController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly OrderService _orderService;

        public WaiterController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, OrderService orderService)
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

            var tables = await _context.DiningTables
                .OrderBy(t => t.TableNumber)
                .ToListAsync();

            var settings = await _context.SystemSettings.ToListAsync();
            ViewBag.Settings = settings.ToDictionary(s => s.Key, s => s.Value);
            ViewBag.Tables = tables;

            return View(categories);
        }

        public async Task<IActionResult> Tables()
        {
            var tables = await _context.DiningTables
                .OrderBy(t => t.TableNumber)
                .ToListAsync();

            var activeOrders = await _context.Orders
                .Include(o => o.Items)
                .Where(o => o.Status == OrderStatus.Pending ||
                            o.Status == OrderStatus.Preparing ||
                            o.Status == OrderStatus.Ready)
                .ToListAsync();

            ViewBag.ActiveOrders = activeOrders;
            return View(tables);
        }

        public async Task<IActionResult> MyOrders(DateTime? date)
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

            var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
            ViewBag.Date = date.Value.ToString("yyyy-MM-dd");
            ViewBag.TotalSales = orders.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.Total);

            return View(orders);
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts(int? categoryId)
        {
            var query = _context.Products
                .Include(p => p.Category)
                .Where(p => p.IsActive && p.IsAvailable);

            if (categoryId.HasValue && categoryId.Value > 0)
                query = query.Where(p => p.CategoryId == categoryId.Value);

            var products = await query
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    nameAr = p.NameAr,
                    price = p.Price,

                    imageUrl = string.IsNullOrWhiteSpace(p.ImageUrl)
                        ? "/images/no-image.png"
                        : p.ImageUrl.StartsWith("/")
                            ? p.ImageUrl
                            : "/" + p.ImageUrl,

                    stockQuantity = p.StockQuantity,
                    trackStock = p.TrackStock,

                    categoryName = p.Category != null ? p.Category.Name : "",
                    categoryNameAr = p.Category != null ? p.Category.NameAr : "",
                    categoryColor = p.Category != null ? p.Category.ColorHex : "#1e3a5f",
                    categoryIcon = p.Category != null ? p.Category.Icon : "🍽️"
                })
                .ToListAsync();

            return Json(products);
        }

        [HttpGet]
        public async Task<IActionResult> GetTables()
        {
            var tables = await _context.DiningTables
                .OrderBy(t => t.TableNumber)
                .ToListAsync();

            var activeOrders = await _context.Orders
                .Include(o => o.Items)
                .Where(o => o.Status == OrderStatus.Pending ||
                            o.Status == OrderStatus.Preparing ||
                            o.Status == OrderStatus.Ready)
                .Select(o => new
                {
                    o.TableId,
                    o.Id,
                    o.OrderNumber,
                    o.Status,
                    ItemCount = o.Items.Count,
                    o.SubTotal
                })
                .ToListAsync();

            var result = tables.Select(t => new
            {
                t.Id,
                t.TableNumber,
                t.Capacity,
                t.Section,
                status = t.Status.ToString(),
                activeOrder = activeOrders.FirstOrDefault(o => o.TableId == t.Id)
            });

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> PlaceOrder([FromBody] WaiterOrderRequest req)
        {
            if (req.Items == null || !req.Items.Any())
                return Json(new { success = false, message = "No items in order" });

            var userId = _userManager.GetUserId(User);
            var settings = await _context.SystemSettings.ToListAsync();
            var taxRate = decimal.Parse(settings.FirstOrDefault(s => s.Key == "TaxRate")?.Value ?? "14");

            var items = new List<OrderItem>();

            foreach (var item in req.Items)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product == null) continue;

                if (product.TrackStock && product.StockQuantity < item.Quantity)
                    return Json(new { success = false, message = $"Not enough stock for {product.Name}" });

                items.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    Product = product,
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
                return Json(new { success = false, message = "No valid products found" });

            if (req.TableId.HasValue && req.OrderType == OrderType.DineIn)
            {
                var table = await _context.DiningTables.FindAsync(req.TableId);
                if (table != null)
                {
                    table.Status = TableStatus.Occupied;
                    _context.Update(table);
                }
            }

            var order = new Order
            {
                CashierId = userId,
                TableId = req.TableId,
                OrderType = req.OrderType,
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
                return Json(new { success = false, message = "Order not found" });

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
                    order.Notes,
                    tableNumber = order.Table?.TableNumber,
                    waiterName = order.Cashier?.UserName,
                    items = order.Items.Select(i => new
                    {
                        i.ProductId,
                        productName = i.Product != null ? i.Product.Name : "",
                        productNameAr = i.Product != null ? i.Product.NameAr : "",
                        i.Quantity,
                        i.UnitPrice,
                        i.TotalPrice,
                        i.Notes
                    })
                }
            });
        }

        [HttpPost]
        public async Task<IActionResult> RequestBill([FromBody] RequestBillRequest req)
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Table)
                .Include(o => o.Cashier)
                .FirstOrDefaultAsync(o => o.Id == req.OrderId);

            if (order == null)
                return Json(new { success = false, message = "Order not found" });

            return Json(new
            {
                success = true,
                order = new
                {
                    order.Id,
                    order.OrderNumber,
                    tableNumber = order.Table?.TableNumber,
                    orderType = order.OrderType.ToString(),
                    status = order.Status.ToString(),
                    order.SubTotal,
                    order.TaxAmount,
                    order.DiscountAmount,
                    order.Total,
                    items = order.Items.Select(i => new
                    {
                        productName = i.Product != null ? i.Product.Name : "",
                        productNameAr = i.Product != null ? i.Product.NameAr : "",
                        i.Quantity,
                        i.UnitPrice,
                        i.TotalPrice
                    })
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetTableOrder(int tableId)
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .Where(o => o.TableId == tableId &&
                           (o.Status == OrderStatus.Pending ||
                            o.Status == OrderStatus.Preparing ||
                            o.Status == OrderStatus.Ready))
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (order == null)
                return Json(new { hasOrder = false });

            return Json(new
            {
                hasOrder = true,
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                status = order.Status.ToString(),
                total = order.Total,
                itemCount = order.Items.Count,
                items = order.Items.Select(i => new
                {
                    productName = i.Product != null ? i.Product.Name : "",
                    productNameAr = i.Product != null ? i.Product.NameAr : "",
                    i.Quantity,
                    i.UnitPrice,
                    i.TotalPrice
                })
            });
        }

        [HttpPost]
        public async Task<IActionResult> FreeTable([FromBody] FreeTableRequest req)
        {
            var table = await _context.DiningTables.FindAsync(req.TableId);

            if (table == null)
                return Json(new { success = false, message = "Table not found" });

            table.Status = TableStatus.Cleaning;
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
    }

    public class WaiterOrderRequest
    {
        public List<WaiterOrderItemRequest> Items { get; set; } = new();
        public int? TableId { get; set; }
        public OrderType OrderType { get; set; } = OrderType.DineIn;
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }
        public string? Notes { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal AmountPaid { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
    }

    public class WaiterOrderItemRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public string? Notes { get; set; }
    }

    public class RequestBillRequest
    {
        public int OrderId { get; set; }
    }

    public class FreeTableRequest
    {
        public int TableId { get; set; }
    }
}
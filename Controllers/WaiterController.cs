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
            try
            {
                if (req == null)
                {
                    return Json(new { success = false, message = "Invalid request data." });
                }

                if (req.Items == null || !req.Items.Any())
                {
                    return Json(new { success = false, message = "Order has no items." });
                }

                int branchId;

                if (req.OrderType == OrderType.DineIn)
                {
                    if (req.TableId == null)
                    {
                        return Json(new { success = false, message = "Please select a table." });
                    }

                    var table = await _context.DiningTables
                        .FirstOrDefaultAsync(t => t.Id == req.TableId.Value);

                    if (table == null)
                    {
                        return Json(new { success = false, message = "Selected table was not found." });
                    }

                    branchId = table.BranchId;
                }
                else
                {
                    var branch = await _context.Branches.FirstOrDefaultAsync();

                    if (branch == null)
                    {
                        return Json(new { success = false, message = "No branch found in database." });
                    }

                    branchId = branch.Id;
                }

                var branchExists = await _context.Branches.AnyAsync(b => b.Id == branchId);

                if (!branchExists)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Invalid BranchId: {branchId}. This branch does not exist."
                    });
                }

                var subtotal = 0m;
                var orderItems = new List<OrderItem>();

                foreach (var item in req.Items)
                {
                    var product = await _context.Products.FindAsync(item.ProductId);

                    if (product == null)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Product with ID {item.ProductId} was not found."
                        });
                    }

                    subtotal += product.Price * item.Quantity;

                    orderItems.Add(new OrderItem
                    {
                        ProductId = product.Id,
                        Quantity = item.Quantity,
                        UnitPrice = product.Price,
                        Notes = item.Notes
                    });
                }

                var discount = req.DiscountAmount;
                var taxable = subtotal - discount;
                if (taxable < 0) taxable = 0;

                var taxRate = 0.14m;
                var taxAmount = taxable * taxRate;
                var total = taxable + taxAmount;

                var order = new Order
                {
                    OrderNumber = "ORD-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
                    CreatedAt = DateTime.Now,

                    TableId = req.OrderType == OrderType.DineIn ? req.TableId : null,
                    OrderType = req.OrderType,
                    Status = OrderStatus.Pending,

                    CustomerName = req.CustomerName,
                    CustomerPhone = req.CustomerPhone,
                    Notes = req.Notes,

                    BranchId = branchId,

                    SubTotal = subtotal,
                    DiscountAmount = discount,
                    TaxRate = taxRate,
                    TaxAmount = taxAmount,
                    Total = total,

                    AmountPaid = req.AmountPaid,
                    PaymentMethod = req.PaymentMethod
                };

                var createdOrder = await _orderService.CreateOrderAsync(order, orderItems);

                return Json(new
                {
                    success = true,
                    orderId = createdOrder.Id,
                    orderNumber = createdOrder.OrderNumber,
                    total = createdOrder.Total
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    details = ex.InnerException?.Message
                });
            }
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
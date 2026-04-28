using Microsoft.EntityFrameworkCore;
using RestaurantERP.Data;
using RestaurantERP.Models;

namespace RestaurantERP.Services
{
    public class OrderService
    {
        private readonly ApplicationDbContext _context;

        public OrderService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Order> CreateOrderAsync(Order order, List<OrderItem> items)
        {
            order.OrderNumber = await GenerateOrderNumber();
            order.Items = items;

            // This Sum is safe because items is already a List in memory.
            order.SubTotal = items.Sum(i => i.TotalPrice);
            order.TaxAmount = order.SubTotal * (order.TaxRate / 100);
            order.Total = order.SubTotal + order.TaxAmount - order.DiscountAmount;
            order.Change = order.AmountPaid - order.Total;

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            return order;
        }

        public async Task<string> GenerateOrderNumber()
        {
            var today = DateTime.Today;
            var count = await _context.Orders.CountAsync(o => o.CreatedAt.Date == today);
            return $"ORD-{today:yyyyMMdd}-{(count + 1):D3}";
        }

        public async Task<bool> UpdateOrderStatusAsync(int orderId, OrderStatus status)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null) return false;

            order.Status = status;

            if (status == OrderStatus.Completed)
                order.CompletedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return true;
        }
    }

    public class AnalyticsService
    {
        private readonly ApplicationDbContext _context;

        public AnalyticsService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DashboardStats> GetDashboardStatsAsync()
        {
            var today = DateTime.Today;
            var thisMonth = new DateTime(today.Year, today.Month, 1);
            var lastMonth = thisMonth.AddMonths(-1);
            var last30DaysStart = today.AddDays(-30);

            // SQLite cannot do Sum(decimal) in SQL.
            // So we load the records first with ToListAsync(), then do Sum/GroupBy in memory.
            var todayOrders = await _context.Orders
                .Where(o => o.CreatedAt.Date == today && o.Status == OrderStatus.Completed)
                .ToListAsync();

            var monthOrders = await _context.Orders
                .Where(o => o.CreatedAt >= thisMonth && o.Status == OrderStatus.Completed)
                .ToListAsync();

            var lastMonthOrders = await _context.Orders
                .Where(o => o.CreatedAt >= lastMonth && o.CreatedAt < thisMonth && o.Status == OrderStatus.Completed)
                .ToListAsync();

            var last30Orders = await _context.Orders
                .Where(o => o.CreatedAt >= last30DaysStart && o.Status == OrderStatus.Completed)
                .ToListAsync();

            var last30Days = last30Orders
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Revenue = g.Sum(o => o.Total),
                    Count = g.Count()
                })
                .OrderBy(x => x.Date)
                .ToList();

            var monthOrderItems = await _context.OrderItems
                .Include(oi => oi.Order)
                .Include(oi => oi.Product)
                    .ThenInclude(p => p!.Category)
                .Where(oi =>
                    oi.Order != null &&
                    oi.Order.CreatedAt >= thisMonth &&
                    oi.Order.Status == OrderStatus.Completed)
                .ToListAsync();

            var topProducts = monthOrderItems
                .GroupBy(oi => new
                {
                    oi.ProductId,
                    Name = oi.Product != null ? oi.Product.Name : "Unknown",
                    NameAr = oi.Product != null ? oi.Product.NameAr : string.Empty
                })
                .Select(g => new ProductSalesDto
                {
                    ProductId = g.Key.ProductId,
                    Name = g.Key.Name,
                    NameAr = g.Key.NameAr,
                    Quantity = g.Sum(oi => oi.Quantity),
                    Revenue = g.Sum(oi => oi.TotalPrice)
                })
                .OrderByDescending(x => x.Revenue)
                .Take(10)
                .ToList();

            var categoryRevenue = monthOrderItems
                .GroupBy(oi => new
                {
                    Name = oi.Product != null && oi.Product.Category != null ? oi.Product.Category.Name : "Other",
                    NameAr = oi.Product != null && oi.Product.Category != null ? oi.Product.Category.NameAr : string.Empty,
                    ColorHex = oi.Product != null && oi.Product.Category != null ? oi.Product.Category.ColorHex : "#000"
                })
                .Select(g => new CategoryRevenueDto
                {
                    Name = g.Key.Name,
                    NameAr = g.Key.NameAr,
                    ColorHex = g.Key.ColorHex,
                    Revenue = g.Sum(oi => oi.TotalPrice)
                })
                .OrderByDescending(x => x.Revenue)
                .ToList();

            var pendingOrders = await _context.Orders
                .Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing)
                .CountAsync();

            var lowStockProducts = await _context.Products
                .Where(p => p.TrackStock && p.StockQuantity <= p.MinStockAlert && p.IsActive)
                .CountAsync();

            var monthRevenue = monthOrders.Sum(o => o.Total);
            var lastMonthRevenue = lastMonthOrders.Sum(o => o.Total);

            var revenueGrowth = lastMonthRevenue > 0
                ? ((monthRevenue - lastMonthRevenue) / lastMonthRevenue) * 100
                : 0;

            return new DashboardStats
            {
                TodaySales = todayOrders.Sum(o => o.Total),
                TodayOrders = todayOrders.Count,
                MonthSales = monthRevenue,
                MonthOrders = monthOrders.Count,
                RevenueGrowth = Math.Round(revenueGrowth, 1),
                PendingOrders = pendingOrders,
                LowStockCount = lowStockProducts,
                DailySales = last30Days.Select(x => new DailySalesDto
                {
                    Date = x.Date,
                    Revenue = x.Revenue,
                    Orders = x.Count
                }).ToList(),
                TopProducts = topProducts,
                CategoryRevenue = categoryRevenue
            };
        }
    }

    // ===== DTOs =====
    public class DashboardStats
    {
        public decimal TodaySales { get; set; }
        public int TodayOrders { get; set; }
        public decimal MonthSales { get; set; }
        public int MonthOrders { get; set; }
        public decimal RevenueGrowth { get; set; }
        public int PendingOrders { get; set; }
        public int LowStockCount { get; set; }
        public List<DailySalesDto> DailySales { get; set; } = new();
        public List<ProductSalesDto> TopProducts { get; set; } = new();
        public List<CategoryRevenueDto> CategoryRevenue { get; set; } = new();
    }

    public class DailySalesDto
    {
        public DateTime Date { get; set; }
        public decimal Revenue { get; set; }
        public int Orders { get; set; }
    }

    public class ProductSalesDto
    {
        public int ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Revenue { get; set; }
    }

    public class CategoryRevenueDto
    {
        public string Name { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "#000";
        public decimal Revenue { get; set; }
    }
}

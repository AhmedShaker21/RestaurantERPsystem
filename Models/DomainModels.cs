using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantERP.Models
{
    // ===================== CATEGORY =====================
    public class Category
    {
        public int Id { get; set; }
        [Required] public string Name { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Icon { get; set; } = "🍽️";
        public string ColorHex { get; set; } = "#FF6B35";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public ICollection<Product> Products { get; set; } = new List<Product>();
    }

    // ===================== PRODUCT =====================
    public class Product
    {
        public int Id { get; set; }
        [Required] public string Name { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? DescriptionAr { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal Price { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal CostPrice { get; set; }
        public int CategoryId { get; set; }
        public Category? Category { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsAvailable { get; set; } = true;
        public int StockQuantity { get; set; } = 0;
        public int MinStockAlert { get; set; } = 5;
        public bool TrackStock { get; set; } = false;
        public string Barcode { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }

    // ===================== TABLE =====================
    public class DiningTable
    {
        public int Id { get; set; }
        [Required] public string TableNumber { get; set; } = string.Empty;
        public int Capacity { get; set; } = 4;
        public TableStatus Status { get; set; } = TableStatus.Available;
        public string? Section { get; set; }
        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }

    public enum TableStatus { Available, Occupied, Reserved, Cleaning, Maintenance }

    // ===================== ORDER =====================
    public class Order
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }
        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public OrderType OrderType { get; set; } = OrderType.DineIn;
        public int? TableId { get; set; }
        public DiningTable? Table { get; set; }
        public string? CashierId { get; set; }
        public ApplicationUser? Cashier { get; set; }
        public int? ShiftId { get; set; }
        public Shift? Shift { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }
        public string? Notes { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal SubTotal { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal TaxRate { get; set; } = 14;
        [Column(TypeName = "decimal(18,2)")] public decimal TaxAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal DiscountAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal Total { get; set; }
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
        [Column(TypeName = "decimal(18,2)")] public decimal AmountPaid { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal Change { get; set; }
        public bool IsPrinted { get; set; } = false;
        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    }

    public enum OrderStatus { Pending, Preparing, Ready, Completed, Cancelled }
    public enum OrderType { DineIn, Takeaway, Delivery }
    public enum PaymentMethod { Cash, Card, Digital }

    // ===================== ORDER ITEM =====================
    public class OrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public Order? Order { get; set; }
        public int ProductId { get; set; }
        public Product? Product { get; set; }
        public int Quantity { get; set; } = 1;
        [Column(TypeName = "decimal(18,2)")] public decimal UnitPrice { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal TotalPrice { get; set; }
        public string? Notes { get; set; }
    }

    // ===================== EXPENSE =====================
    public class Expense
    {
        public int Id { get; set; }
        [Required] public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
        public string Category { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Now;
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
        public string? RecordedById { get; set; }
        public int? ShiftId { get; set; }
        public Shift? Shift { get; set; }
        public ApplicationUser? RecordedBy { get; set; }
        public string? CreatedById { get; set; }
        public ApplicationUser? CreatedBy { get; set; }
    }

    // ===================== INVENTORY =====================
    public class InventoryLog
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public Product? Product { get; set; }
        public int QuantityChange { get; set; }
        public int QuantityBefore { get; set; }
        public int QuantityAfter { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string? CreatedById { get; set; }
    }

    // ===================== SETTINGS =====================
    public class SystemSettings
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    // ===================== SHIFT =====================
    public class Shift
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal OpeningCash { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal ClosingCash { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal TotalSales { get; set; }
        public bool IsClosed { get; set; } = false;
        public string? Notes { get; set; }
        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    }
}

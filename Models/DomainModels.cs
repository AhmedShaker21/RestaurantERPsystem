using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantERP.Models
{
    // ===================== BRANCH =====================
    public class Branch
    {
        public int Id { get; set; }
        [Required] public string Name { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? ManagerId { get; set; }
        public ApplicationUser? Manager { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsMainBranch { get; set; } = false;
        public string ColorHex { get; set; } = "#2563a8";
        public string? Icon { get; set; } = "🏢";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public ICollection<DiningTable> Tables { get; set; } = new List<DiningTable>();
        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
        public ICollection<Shift> Shifts { get; set; } = new List<Shift>();
        public ICollection<UserBranch> UserBranches { get; set; } = new List<UserBranch>();
        public ICollection<ProductBranch> ProductBranches { get; set; } = new List<ProductBranch>();
    }

    public class UserBranch
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }
        public int BranchId { get; set; }
        public Branch? Branch { get; set; }
        public bool IsPrimary { get; set; } = false;
    }

    // NEW: Product <-> Branch many-to-many
    public class ProductBranch
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public Product? Product { get; set; }
        public int BranchId { get; set; }
        public Branch? Branch { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal? OverridePrice { get; set; }
    }

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
        public bool SkipKitchen { get; set; } = false;
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

        // Box/Unit pricing
        public bool SellByBox { get; set; } = false;
        public int UnitsPerBox { get; set; } = 1;
        [Column(TypeName = "decimal(18,2)")] public decimal BoxCostPrice { get; set; } = 0;
        [Column(TypeName = "decimal(18,2)")] public decimal BoxSellPrice { get; set; } = 0;
        public string? BoxBarcode { get; set; }

        // Per-product tax override (null = use system default 14%)
        [Column(TypeName = "decimal(5,2)")] public decimal? TaxRateOverride { get; set; }

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
        public ICollection<ProductBranch> ProductBranches { get; set; } = new List<ProductBranch>();

        [NotMapped] public decimal EffectivePrice => SellByBox && UnitsPerBox > 0 ? BoxSellPrice / UnitsPerBox : Price;
        [NotMapped] public decimal EffectiveCost => SellByBox && UnitsPerBox > 0 ? BoxCostPrice / UnitsPerBox : CostPrice;
        [NotMapped] public decimal EffectiveTaxRate => TaxRateOverride ?? 14m;
    }

    // ===================== TABLE =====================
    public class DiningTable
    {
        public int Id { get; set; }
        [Required] public string TableNumber { get; set; } = string.Empty;
        public int Capacity { get; set; } = 4;
        public TableStatus Status { get; set; } = TableStatus.Available;
        public string? Section { get; set; }
        public int BranchId { get; set; }
        public Branch? Branch { get; set; }
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
        public int BranchId { get; set; }
        public Branch? Branch { get; set; }
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

    public enum OrderStatus { Pending, Preparing, Ready, Completed, Cancelled, Refunded, PartialRefund }
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
        public string ProductName { get; set; } = string.Empty;
        public string ProductNameAr { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        [Column(TypeName = "decimal(18,2)")] public decimal UnitPrice { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal TotalPrice { get; set; }
        public string? Notes { get; set; }
        public bool SkipKitchen { get; set; } = false;
    }

    // ===================== REFUND =====================
    public class Refund
    {
        public int Id { get; set; }
        public string RefundNumber { get; set; } = string.Empty;
        public int OriginalOrderId { get; set; }
        public Order? OriginalOrder { get; set; }
        public string? ProcessedById { get; set; }
        public ApplicationUser? ProcessedBy { get; set; }
        public int BranchId { get; set; }
        public Branch? Branch { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public RefundType RefundType { get; set; } = RefundType.Full;
        public RefundMethod RefundMethod { get; set; } = RefundMethod.Cash;
        [Column(TypeName = "decimal(18,2)")] public decimal RefundAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal RefundTax { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal RefundTotal { get; set; }
        public string? Reason { get; set; }
        public string? Notes { get; set; }
        public RefundStatus Status { get; set; } = RefundStatus.Completed;
        public ICollection<RefundItem> Items { get; set; } = new List<RefundItem>();
    }

    public class RefundItem
    {
        public int Id { get; set; }
        public int RefundId { get; set; }
        public Refund? Refund { get; set; }
        public int OrderItemId { get; set; }
        public OrderItem? OrderItem { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductNameAr { get; set; } = string.Empty;
        public int Quantity { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal UnitPrice { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal TotalPrice { get; set; }
    }

    public enum RefundType { Full, Partial }
    public enum RefundMethod { Cash, Card, Digital, StoreCredit }
    public enum RefundStatus { Pending, Completed, Rejected }

    // ===================== EXPENSE =====================
    public class Expense
    {
        public int Id { get; set; }
        [Required] public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? PaymentMethod { get; set; }
        public string? Notes { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;
        public string? CreatedById { get; set; }
        public ApplicationUser? CreatedBy { get; set; }
        public int BranchId { get; set; }
        public Branch? Branch { get; set; }
        public ApplicationUser? RecordedBy { get; set; }
        public string? RecordedById { get; set; }
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
        public int? BranchId { get; set; }
        public Branch? Branch { get; set; }
    }

    // ===================== SHIFT =====================
    public class Shift
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }
        public int BranchId { get; set; }
        public Branch? Branch { get; set; }
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal OpeningCash { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal ClosingCash { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal TotalSales { get; set; }
        public int TotalOrders { get; set; }
        public bool IsClosed { get; set; } = false;
        public string? Notes { get; set; }
        public bool IsActive => !IsClosed;
        public ApplicationUser? Cashier => User;
        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }
}

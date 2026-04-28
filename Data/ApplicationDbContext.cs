using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RestaurantERP.Models;

namespace RestaurantERP.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<DiningTable> DiningTables { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<InventoryLog> InventoryLogs { get; set; }
        public DbSet<SystemSettings> SystemSettings { get; set; }
        public DbSet<Shift> Shifts { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Order>()
                .HasOne(o => o.Table)
                .WithMany(t => t.Orders)
                .HasForeignKey(o => o.TableId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Order>()
                .HasOne(o => o.Cashier)
                .WithMany()
                .HasForeignKey(o => o.CashierId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<OrderItem>()
                .HasOne(oi => oi.Product)
                .WithMany(p => p.OrderItems)
                .HasForeignKey(oi => oi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Expense>()
                .HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<InventoryLog>()
                .HasOne(il => il.Product)
                .WithMany()
                .HasForeignKey(il => il.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Shift>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Order>()
                .HasOne(o => o.Shift)
                .WithMany(s => s.Orders)
                .HasForeignKey(o => o.ShiftId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<Expense>()
                .HasOne(e => e.Shift)
                .WithMany(s => s.Expenses)
                .HasForeignKey(e => e.ShiftId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}

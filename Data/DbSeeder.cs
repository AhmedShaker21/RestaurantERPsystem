using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantERP.Models;

namespace RestaurantERP.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            await context.Database.MigrateAsync();

            // Seed Roles
            string[] roles = { "Admin", "Manager", "Cashier", "Kitchen", "Waiter", "محصل" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            // Seed Admin
            if (await userManager.FindByEmailAsync("admin@restaurant.com") == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = "admin@restaurant.com",
                    Email = "admin@restaurant.com",
                    FullName = "System Administrator",
                    FullNameAr = "مدير النظام",
                    EmailConfirmed = true,
                    IsActive = true
                };
                await userManager.CreateAsync(admin, "Admin@123");
                await userManager.AddToRoleAsync(admin, "Admin");
            }

            // Seed Manager
            if (await userManager.FindByEmailAsync("manager@restaurant.com") == null)
            {
                var manager = new ApplicationUser
                {
                    UserName = "manager@restaurant.com",
                    Email = "manager@restaurant.com",
                    FullName = "Ahmed Manager",
                    FullNameAr = "أحمد المدير",
                    EmailConfirmed = true,
                    IsActive = true
                };
                await userManager.CreateAsync(manager, "Manager@123");
                await userManager.AddToRoleAsync(manager, "Manager");
            }

            // Seed Cashier
            if (await userManager.FindByEmailAsync("cashier@restaurant.com") == null)
            {
                var cashier = new ApplicationUser
                {
                    UserName = "cashier@restaurant.com",
                    Email = "cashier@restaurant.com",
                    FullName = "Mohamed Cashier",
                    FullNameAr = "محمد الكاشير",
                    EmailConfirmed = true,
                    IsActive = true
                };
                await userManager.CreateAsync(cashier, "Cashier@123");
                await userManager.AddToRoleAsync(cashier, "Cashier");
            }

            // Seed Kitchen
            if (await userManager.FindByEmailAsync("kitchen@restaurant.com") == null)
            {
                var kitchen = new ApplicationUser
                {
                    UserName = "kitchen@restaurant.com",
                    Email = "kitchen@restaurant.com",
                    FullName = "Kitchen Staff",
                    FullNameAr = "موظف المطبخ",
                    EmailConfirmed = true,
                    IsActive = true
                };
                await userManager.CreateAsync(kitchen, "Kitchen@123");
                await userManager.AddToRoleAsync(kitchen, "Kitchen");
            }

            // ── Seed Branches ─────────────────────────────────────
            if (!context.Branches.Any())
            {
                var managerUser = await userManager.FindByEmailAsync("manager@restaurant.com");

                var branches = new List<Branch>
                {
                    new() {
                        Name = "Main Branch", NameAr = "الفرع بجانب البوابه",
                        Address = "Cairo, Egypt", Phone = "02-12345678",
                        Email = "main@restaurant.com",
                        ManagerId = managerUser?.Id,
                        IsActive = true, IsMainBranch = true,
                        ColorHex = "#2563a8", Icon = "🏢"
                    },
                    new() {
                        Name = "Branch 2", NameAr = "فرع قاعة الافراح",
                        Address = "Cairo", Phone = "02-22345678",
                        Email = "Branch02@restaurant.com",
                        IsActive = true, IsMainBranch = false,
                        ColorHex = "#22c55e", Icon = "🏪"
                    },
                    new() {
                        Name = "Branch 3", NameAr = "فرع اخر النادي",
                        Address = "Cairo", Phone = "02-32345678",
                        Email = "Branch03@restaurant.com",
                        IsActive = true, IsMainBranch = false,
                        ColorHex = "#f59e0b", Icon = "🏬"
                    }
                };
                context.Branches.AddRange(branches);
                await context.SaveChangesAsync();

                // Assign all users to main branch, some to branch 2
                var mainBranchId = branches[0].Id;
                var branch2Id = branches[1].Id;

                var allUsers = userManager.Users.ToList();
                foreach (var user in allUsers)
                {
                    context.UserBranches.Add(new UserBranch
                    {
                        UserId = user.Id,
                        BranchId = mainBranchId,
                        IsPrimary = true
                    });
                    // Also assign cashier to branch 2
                    var userRoles = await userManager.GetRolesAsync(user);
                    if (roles.Contains("Cashier") || roles.Contains("Manager"))
                    {
                        context.UserBranches.Add(new UserBranch
                        {
                            UserId = user.Id,
                            BranchId = branch2Id,
                            IsPrimary = false
                        });
                    }
                    // Set default branch
                    user.DefaultBranchId = mainBranchId;
                    await userManager.UpdateAsync(user);
                }
                await context.SaveChangesAsync();
            }

            // ── Seed Categories ───────────────────────────────────
            if (!context.Categories.Any())
            {
                var categories = new List<Category>
                {
                    new() { Name = "Hot Drinks",  NameAr = "مشروبات ساخنة", Icon = "☕", ColorHex = "#8B4513" },
                    new() { Name = "Cold Drinks", NameAr = "مشروبات باردة", Icon = "🧊", ColorHex = "#00BFFF" },
                    new() { Name = "Main Dishes", NameAr = "أطباق رئيسية",  Icon = "🍽️", ColorHex = "#FF6B35" },
                    new() { Name = "Sandwiches",  NameAr = "سندوتشات",      Icon = "🥪", ColorHex = "#FFD700" },
                    new() { Name = "Salads",      NameAr = "سلطات",          Icon = "🥗", ColorHex = "#32CD32" },
                    new() { Name = "Desserts",    NameAr = "حلويات",         Icon = "🍰", ColorHex = "#FF69B4" },
                    new() { Name = "Juices",      NameAr = "عصائر",          Icon = "🥤", ColorHex = "#FFA500" },
                    new() { Name = "Soups",       NameAr = "شوربات",         Icon = "🥣", ColorHex = "#DC143C" },
                };
                context.Categories.AddRange(categories);
                await context.SaveChangesAsync();
            }

            // Seed Products
            if (!context.Products.Any())
            {
                var cats = await context.Categories.ToListAsync();
                var catDict = cats.ToDictionary(c => c.Name);

                var products = new List<Product>
                {
                    // Hot Drinks
                    new() { Name = "Egyptian Tea", NameAr = "شاي مصري", Price = 5, CostPrice = 1, CategoryId = catDict["Hot Drinks"].Id, IsAvailable = true, StockQuantity = 100, TrackStock = false },
                    new() { Name = "Nescafe", NameAr = "نسكافيه", Price = 15, CostPrice = 5, CategoryId = catDict["Hot Drinks"].Id, IsAvailable = true },
                    new() { Name = "Turkish Coffee", NameAr = "قهوة تركي", Price = 20, CostPrice = 7, CategoryId = catDict["Hot Drinks"].Id, IsAvailable = true },
                    new() { Name = "Karak Tea", NameAr = "شاي كرك", Price = 15, CostPrice = 4, CategoryId = catDict["Hot Drinks"].Id, IsAvailable = true },
                    new() { Name = "Hot Chocolate", NameAr = "شوكولاتة ساخنة", Price = 25, CostPrice = 10, CategoryId = catDict["Hot Drinks"].Id, IsAvailable = true },
                    // Cold Drinks
                    new() { Name = "Pepsi", NameAr = "بيبسي", Price = 15, CostPrice = 7, CategoryId = catDict["Cold Drinks"].Id, IsAvailable = true, TrackStock = true, StockQuantity = 50 },
                    new() { Name = "7Up", NameAr = "سبن أب", Price = 15, CostPrice = 7, CategoryId = catDict["Cold Drinks"].Id, IsAvailable = true, TrackStock = true, StockQuantity = 40 },
                    new() { Name = "Water Bottle", NameAr = "مياه معدنية", Price = 5, CostPrice = 2, CategoryId = catDict["Cold Drinks"].Id, IsAvailable = true, TrackStock = true, StockQuantity = 100 },
                    new() { Name = "Lemon Mint", NameAr = "ليمون بالنعناع", Price = 20, CostPrice = 5, CategoryId = catDict["Cold Drinks"].Id, IsAvailable = true },
                    // Main Dishes
                    new() { Name = "Grilled Chicken", NameAr = "فراخ مشوية", Price = 85, CostPrice = 35, CategoryId = catDict["Main Dishes"].Id, IsAvailable = true },
                    new() { Name = "Kofta", NameAr = "كفتة", Price = 65, CostPrice = 28, CategoryId = catDict["Main Dishes"].Id, IsAvailable = true },
                    new() { Name = "Fish Fillet", NameAr = "فيليه سمك", Price = 90, CostPrice = 40, CategoryId = catDict["Main Dishes"].Id, IsAvailable = true },
                    new() { Name = "Chicken Tikka", NameAr = "تيكا دجاج", Price = 95, CostPrice = 42, CategoryId = catDict["Main Dishes"].Id, IsAvailable = true },
                    new() { Name = "Mixed Grill", NameAr = "مشاوي مشكلة", Price = 150, CostPrice = 65, CategoryId = catDict["Main Dishes"].Id, IsAvailable = true },
                    // Sandwiches
                    new() { Name = "Falafel Sandwich", NameAr = "سندوتش فلافل", Price = 10, CostPrice = 3, CategoryId = catDict["Sandwiches"].Id, IsAvailable = true },
                    new() { Name = "Hawawshi", NameAr = "هوواوشي", Price = 35, CostPrice = 15, CategoryId = catDict["Sandwiches"].Id, IsAvailable = true },
                    new() { Name = "Club Sandwich", NameAr = "كلوب سندوتش", Price = 55, CostPrice = 22, CategoryId = catDict["Sandwiches"].Id, IsAvailable = true },
                    new() { Name = "Chicken Burger", NameAr = "برجر دجاج", Price = 65, CostPrice = 28, CategoryId = catDict["Sandwiches"].Id, IsAvailable = true },
                    // Salads
                    new() { Name = "Green Salad", NameAr = "سلطة خضراء", Price = 25, CostPrice = 8, CategoryId = catDict["Salads"].Id, IsAvailable = true },
                    new() { Name = "Fattoush", NameAr = "فتوش", Price = 30, CostPrice = 10, CategoryId = catDict["Salads"].Id, IsAvailable = true },
                    new() { Name = "Caesar Salad", NameAr = "سيزر سلطة", Price = 45, CostPrice = 18, CategoryId = catDict["Salads"].Id, IsAvailable = true },
                    // Desserts
                    new() { Name = "Om Ali", NameAr = "أم علي", Price = 35, CostPrice = 12, CategoryId = catDict["Desserts"].Id, IsAvailable = true },
                    new() { Name = "Kunafa", NameAr = "كنافة", Price = 40, CostPrice = 15, CategoryId = catDict["Desserts"].Id, IsAvailable = true },
                    new() { Name = "Ice Cream", NameAr = "آيس كريم", Price = 25, CostPrice = 10, CategoryId = catDict["Desserts"].Id, IsAvailable = true },
                    // Juices
                    new() { Name = "Fresh Orange Juice", NameAr = "عصير برتقال طازج", Price = 30, CostPrice = 10, CategoryId = catDict["Juices"].Id, IsAvailable = true },
                    new() { Name = "Mango Juice", NameAr = "عصير مانجو", Price = 35, CostPrice = 12, CategoryId = catDict["Juices"].Id, IsAvailable = true },
                    new() { Name = "Strawberry Juice", NameAr = "عصير فراولة", Price = 30, CostPrice = 12, CategoryId = catDict["Juices"].Id, IsAvailable = true },
                    // Soups
                    new() { Name = "Lentil Soup", NameAr = "شوربة عدس", Price = 25, CostPrice = 8, CategoryId = catDict["Soups"].Id, IsAvailable = true },
                    new() { Name = "Chicken Soup", NameAr = "شوربة دجاج", Price = 30, CostPrice = 10, CategoryId = catDict["Soups"].Id, IsAvailable = true },
                };
                context.Products.AddRange(products);
                await context.SaveChangesAsync();
            }

            // Seed Tables
            if (!context.DiningTables.Any())
            {
                var mainBranch = await context.Branches.FirstOrDefaultAsync(b => b.IsMainBranch);
                var branch2 = await context.Branches.FirstOrDefaultAsync(b => !b.IsMainBranch && b.IsActive);
                var mainBranchId = mainBranch?.Id ?? 1;
                var b2Id = branch2?.Id ?? mainBranchId;

                var tables = new List<DiningTable>();
                // 10 tables for main branch
                for (int i = 1; i <= 10; i++)
                {
                    tables.Add(new DiningTable
                    {
                        TableNumber = i.ToString("D2"),
                        Capacity = i <= 4 ? 2 : i <= 8 ? 4 : 6,
                        Section = i <= 4 ? "Indoor A" : i <= 8 ? "Indoor B" : "Outdoor",
                        Status = TableStatus.Available,
                        BranchId = mainBranchId
                    });
                }
                // 5 tables for branch 2
                for (int i = 1; i <= 5; i++)
                {
                    tables.Add(new DiningTable
                    {
                        TableNumber = i.ToString("D2"),
                        Capacity = i <= 2 ? 2 : 4,
                        Section = "Main Hall",
                        Status = TableStatus.Available,
                        BranchId = b2Id
                    });
                }
                context.DiningTables.AddRange(tables);
                await context.SaveChangesAsync();
            }

            // Seed System Settings
            if (!context.SystemSettings.Any())
            {
                var settings = new List<SystemSettings>
                {
                    new() { Key = "OrgName", Value = "نادي مصنع الطائرات" },
                    new() { Key = "OrgNameEn", Value = "Aircraft Factory Club" },
                    new() { Key = "TaxRate", Value = "14" },
                    new() { Key = "Currency", Value = "EGP" },
                    new() { Key = "CurrencyAr", Value = "ج.م" },
                    new() { Key = "Phone", Value = "02-12345678" },
                    new() { Key = "Address", Value = "القاهرة، مصر" },
                    new() { Key = "FooterNote", Value = "شكراً لزيارتكم - Thank you for your visit" },
                };
                context.SystemSettings.AddRange(settings);
                await context.SaveChangesAsync();
            }

            // Seed sample orders for analytics
            if (!context.Orders.Any())
            {
                var random = new Random(42);
                var products = await context.Products.ToListAsync();
                var cashierUser = await userManager.FindByEmailAsync("cashier@restaurant.com");
                var tables = await context.DiningTables.ToListAsync();

                for (int day = 29; day >= 1; day--)
                {
                    int ordersPerDay = random.Next(8, 25);
                    for (int o = 0; o < ordersPerDay; o++)
                    {
                        var orderProducts = products.OrderBy(_ => random.Next()).Take(random.Next(1, 5)).ToList();
                        var items = orderProducts.Select(p => new OrderItem
                        {
                            ProductId = p.Id,
                            Quantity = random.Next(1, 4),
                            UnitPrice = p.Price,
                            TotalPrice = p.Price * random.Next(1, 4)
                        }).ToList();

                        var sub = items.Sum(i => i.TotalPrice);
                        var tax = sub * 0.14m;
                        var discount = random.Next(0, 3) == 0 ? sub * 0.1m : 0;
                        var total = sub + tax - discount;

                        var branchIds = context.Branches.Select(b => b.Id).ToList();
                        var orderBranchId = branchIds.Count > 0 ? branchIds[random.Next(branchIds.Count)] : 1;

                        var order = new Order
                        {
                            OrderNumber = $"ORD-{DateTime.Now.AddDays(-day):yyyyMMdd}-{o + 1:D3}",
                            CreatedAt = DateTime.Now.AddDays(-day).AddHours(random.Next(8, 22)).AddMinutes(random.Next(0, 60)),
                            CompletedAt = DateTime.Now.AddDays(-day).AddHours(random.Next(8, 22)).AddMinutes(random.Next(30, 90)),
                            Status = OrderStatus.Completed,
                            OrderType = (OrderType)random.Next(0, 3),
                            TableId = random.Next(0, 2) == 0 ? tables[random.Next(tables.Count)].Id : null,
                            CashierId = cashierUser?.Id,
                            BranchId = orderBranchId,
                            SubTotal = sub,
                            TaxRate = 14,
                            TaxAmount = tax,
                            DiscountAmount = discount,
                            Total = total,
                            AmountPaid = total + random.Next(0, 2) * 10,
                            Change = random.Next(0, 2) * 10m,
                            PaymentMethod = (PaymentMethod)random.Next(0, 3),
                            IsPrinted = true,
                            Items = items
                        };
                        context.Orders.Add(order);
                    }
                }
                await context.SaveChangesAsync();
            }
        }
    }
}
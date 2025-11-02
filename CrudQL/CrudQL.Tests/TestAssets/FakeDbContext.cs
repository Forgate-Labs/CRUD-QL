using Microsoft.EntityFrameworkCore;

namespace CrudQL.Tests.TestAssets;

public class FakeDbContext : DbContext
{
    public FakeDbContext(DbContextOptions<FakeDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();
}

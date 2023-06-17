using API.Entity;
using Microsoft.EntityFrameworkCore;

namespace hc_test_proj.Database;

public class MyDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Review> Reviews { get; set; }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .UseNpgsql("Server=127.0.0.1;Port=5432;Database=hc_test_db;User Id=postgres;Password=12345;")
            .UseLazyLoadingProxies(useLazyLoadingProxies: true)
            ;
    }
}
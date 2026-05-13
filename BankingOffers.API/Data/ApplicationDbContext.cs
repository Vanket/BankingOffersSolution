using BankingOffers.API.Entities;
using Microsoft.EntityFrameworkCore;

namespace BankingOffers.API.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
        
    }
    public DbSet<LoanProduct> LoanProducts { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<MortgageApplication> MortgageApplications { get; set; }
    public DbSet<Favorite> Favorites { get; set; }
}

using Groceries.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Groceries.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<HouseholdCategory> Categories => Set<HouseholdCategory>();
    public DbSet<GroceryItem> GroceryItems => Set<GroceryItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Household>(household =>
        {
            household.HasIndex(value => value.InviteCode).IsUnique();
            household.Property(value => value.Name).HasMaxLength(80);
            household.Property(value => value.InviteCode).HasMaxLength(8);
        });

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(value => value.DisplayName).HasMaxLength(60);
            user.HasOne(value => value.Household)
                .WithMany(value => value.Members)
                .HasForeignKey(value => value.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<HouseholdCategory>(category =>
        {
            category.Property(value => value.Name).HasMaxLength(32);
            category.Property(value => value.Icon).HasMaxLength(16);
            category.HasIndex(value => new { value.HouseholdId, value.Name }).IsUnique();
            category.HasOne(value => value.Household)
                .WithMany(value => value.Categories)
                .HasForeignKey(value => value.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GroceryItem>(item =>
        {
            item.Property(value => value.Name).HasMaxLength(80);
            item.Property(value => value.Unit).HasMaxLength(24);
            item.Property(value => value.Category).HasMaxLength(32);
            item.HasOne(value => value.Household)
                .WithMany(value => value.Items)
                .HasForeignKey(value => value.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
            item.HasOne(value => value.CreatedByUser)
                .WithMany()
                .HasForeignKey(value => value.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

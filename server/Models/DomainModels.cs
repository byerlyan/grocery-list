using Microsoft.AspNetCore.Identity;

namespace Groceries.Api.Models;

public sealed class ApplicationUser : IdentityUser
{
    public required string DisplayName { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
}

public sealed class Household
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string InviteCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<ApplicationUser> Members { get; set; } = [];
    public List<GroceryItem> Items { get; set; } = [];
    public List<HouseholdCategory> Categories { get; set; } = [];
}

public sealed class HouseholdCategory
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Icon { get; set; }
    public int SortOrder { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
}

public sealed class GroceryItem
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public int Quantity { get; set; }
    public string Unit { get; set; } = "";
    public string Category { get; set; } = "Other";
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }
}

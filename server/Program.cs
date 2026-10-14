using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Groceries.Api.Data;
using Groceries.Api.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var workingDirectory = Directory.GetCurrentDirectory();
var contentRoot = Directory.Exists(Path.Combine(workingDirectory, "server"))
    ? Path.Combine(workingDirectory, "server")
    : workingDirectory;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
    WebRootPath = "wwwroot"
});

var dataPath = Path.Combine(contentRoot, "data");
Directory.CreateDirectory(dataPath);
var defaultDatabasePath = Path.Combine(dataPath, "groceries.db");
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? $"Data Source={defaultDatabasePath}";
var dataProtectionPath = builder.Configuration["DataProtection:Path"]
    ?? Path.Combine(dataPath, "keys");
Directory.CreateDirectory(dataProtectionPath);

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddDataProtection()
    .SetApplicationName("MarketList")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "market-list.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await database.Database.EnsureCreatedAsync();
    await database.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "Categories" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Categories" PRIMARY KEY,
            "Name" TEXT NOT NULL,
            "Icon" TEXT NOT NULL,
            "SortOrder" INTEGER NOT NULL,
            "HouseholdId" TEXT NOT NULL,
            CONSTRAINT "FK_Categories_Households_HouseholdId"
                FOREIGN KEY ("HouseholdId") REFERENCES "Households" ("Id") ON DELETE CASCADE
        );
        """);
    await database.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_Categories_HouseholdId\" ON \"Categories\" (\"HouseholdId\");");
    await database.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Categories_HouseholdId_Name\" ON \"Categories\" (\"HouseholdId\", \"Name\");");

    var householdIds = await database.Households.Select(household => household.Id).ToListAsync();
    foreach (var householdId in householdIds)
    {
        var existingNames = await database.Categories
            .Where(category => category.HouseholdId == householdId)
            .Select(category => category.Name)
            .ToListAsync();
        var missingCategories = CreateDefaultCategories(householdId)
            .Where(category => !existingNames.Contains(category.Name, StringComparer.OrdinalIgnoreCase));
        database.Categories.AddRange(missingCategories);
    }
    await database.SaveChangesAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

var auth = app.MapGroup("/api/auth");

auth.MapPost("/register", async (
    RegisterRequest request,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AppDbContext database) =>
{
    var validation = ValidateRegistration(request);
    if (validation is not null) return Results.BadRequest(new { message = validation });

    var household = new Household
    {
        Id = Guid.NewGuid(),
        Name = string.IsNullOrWhiteSpace(request.HouseholdName)
            ? $"{request.DisplayName.Trim()}'s household"
            : request.HouseholdName.Trim(),
        InviteCode = await CreateUniqueInviteCodeAsync(database),
        CreatedAt = DateTimeOffset.UtcNow
    };
    household.Categories.AddRange(CreateDefaultCategories(household.Id));

    await using var transaction = await database.Database.BeginTransactionAsync();
    database.Households.Add(household);
    await database.SaveChangesAsync();

    var user = new ApplicationUser
    {
        UserName = request.Email.Trim(),
        Email = request.Email.Trim(),
        DisplayName = request.DisplayName.Trim(),
        HouseholdId = household.Id
    };
    var result = await userManager.CreateAsync(user, request.Password);
    if (!result.Succeeded)
    {
        await transaction.RollbackAsync();
        return Results.BadRequest(new { message = string.Join(" ", result.Errors.Select(error => error.Description)) });
    }

    await transaction.CommitAsync();
    await signInManager.SignInAsync(user, isPersistent: true);
    return Results.Ok(await BuildSessionAsync(user.Id, database));
});

auth.MapPost("/login", async (
    LoginRequest request,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AppDbContext database) =>
{
    var user = await userManager.FindByEmailAsync(request.Email.Trim());
    if (user is null) return Results.Unauthorized();

    var result = await signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: false);
    return result.Succeeded
        ? Results.Ok(await BuildSessionAsync(user.Id, database))
        : Results.Unauthorized();
});

auth.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.NoContent();
}).RequireAuthorization();

auth.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext database) =>
{
    var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
    return Results.Ok(await BuildSessionAsync(userId, database));
}).RequireAuthorization();

var householdApi = app.MapGroup("/api/household").RequireAuthorization();

householdApi.MapPost("/join", async (
    JoinHouseholdRequest request,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext database) =>
{
    var code = request.InviteCode.Trim().ToUpperInvariant();
    var target = await database.Households.SingleOrDefaultAsync(household => household.InviteCode == code);
    if (target is null) return Results.BadRequest(new { message = "That invite code is not valid." });

    var user = await userManager.GetUserAsync(principal);
    if (user is null) return Results.Unauthorized();
    if (user.HouseholdId == target.Id) return Results.Ok(await BuildSessionAsync(user.Id, database));

    var previousHouseholdId = user.HouseholdId;
    var isOnlyMember = !await database.Users.AnyAsync(member =>
        member.HouseholdId == previousHouseholdId && member.Id != user.Id);
    var previousHasItems = await database.GroceryItems.AnyAsync(item => item.HouseholdId == previousHouseholdId);
    if (isOnlyMember && previousHasItems)
    {
        return Results.BadRequest(new
        {
            message = "Your current household still has items. Clear the list before joining another household."
        });
    }

    user.HouseholdId = target.Id;
    var updateResult = await userManager.UpdateAsync(user);
    if (!updateResult.Succeeded)
    {
        return Results.BadRequest(new { message = "We could not move your account to that household." });
    }

    var previousHasMembers = await database.Users.AnyAsync(member => member.HouseholdId == previousHouseholdId);
    if (!previousHasMembers)
    {
        var previousHousehold = await database.Households.FindAsync(previousHouseholdId);
        if (previousHousehold is not null)
        {
            database.Households.Remove(previousHousehold);
            await database.SaveChangesAsync();
        }
    }

    return Results.Ok(await BuildSessionAsync(user.Id, database));
});

householdApi.MapPost("/invite-code", async (ClaimsPrincipal principal, AppDbContext database) =>
{
    var householdId = await GetHouseholdIdAsync(principal, database);
    var household = await database.Households.FindAsync(householdId);
    if (household is null) return Results.NotFound();

    household.InviteCode = await CreateUniqueInviteCodeAsync(database);
    await database.SaveChangesAsync();
    return Results.Ok(new { household.InviteCode });
});

var categoriesApi = app.MapGroup("/api/categories").RequireAuthorization();

categoriesApi.MapGet("/", async (ClaimsPrincipal principal, AppDbContext database) =>
{
    var householdId = await GetHouseholdIdAsync(principal, database);
    var categories = await database.Categories
        .AsNoTracking()
        .Where(category => category.HouseholdId == householdId)
        .OrderBy(category => category.SortOrder)
        .ThenBy(category => category.Name)
        .Select(category => new CategoryResponse(
            category.Id,
            category.Name,
            category.Icon,
            category.SortOrder))
        .ToListAsync();
    return Results.Ok(categories);
});

categoriesApi.MapPost("/", async (
    CategoryInput input,
    ClaimsPrincipal principal,
    AppDbContext database) =>
{
    var error = ValidateCategory(input);
    if (error is not null) return Results.BadRequest(new { message = error });

    var householdId = await GetHouseholdIdAsync(principal, database);
    var name = input.Name.Trim();
    var normalizedName = name.ToLower();
    var alreadyExists = await database.Categories.AnyAsync(category =>
        category.HouseholdId == householdId && category.Name.ToLower() == normalizedName);
    if (alreadyExists) return Results.BadRequest(new { message = "That category already exists." });

    var lastSortOrder = await database.Categories
        .Where(category => category.HouseholdId == householdId)
        .Select(category => (int?)category.SortOrder)
        .MaxAsync() ?? -1;
    var category = new HouseholdCategory
    {
        Id = Guid.NewGuid(),
        Name = name,
        Icon = input.Icon.Trim(),
        SortOrder = lastSortOrder + 1,
        HouseholdId = householdId
    };
    database.Categories.Add(category);
    await database.SaveChangesAsync();
    return Results.Created($"/api/categories/{category.Id}", ToCategoryResponse(category));
});

categoriesApi.MapPut("/{id:guid}", async (
    Guid id,
    CategoryInput input,
    ClaimsPrincipal principal,
    AppDbContext database) =>
{
    var error = ValidateCategory(input);
    if (error is not null) return Results.BadRequest(new { message = error });

    var householdId = await GetHouseholdIdAsync(principal, database);
    var category = await database.Categories.SingleOrDefaultAsync(value =>
        value.Id == id && value.HouseholdId == householdId);
    if (category is null) return Results.NotFound();

    var name = input.Name.Trim();
    var normalizedName = name.ToLower();
    var alreadyExists = await database.Categories.AnyAsync(value =>
        value.HouseholdId == householdId && value.Id != id && value.Name.ToLower() == normalizedName);
    if (alreadyExists) return Results.BadRequest(new { message = "That category already exists." });

    await using var transaction = await database.Database.BeginTransactionAsync();
    var previousName = category.Name;
    category.Name = name;
    category.Icon = input.Icon.Trim();
    if (!string.Equals(previousName, name, StringComparison.Ordinal))
    {
        await database.GroceryItems
            .Where(item => item.HouseholdId == householdId && item.Category == previousName)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Category, name));
    }
    await database.SaveChangesAsync();
    await transaction.CommitAsync();
    return Results.Ok(ToCategoryResponse(category));
});

categoriesApi.MapDelete("/{id:guid}", async (
    Guid id,
    ClaimsPrincipal principal,
    AppDbContext database) =>
{
    var householdId = await GetHouseholdIdAsync(principal, database);
    var householdCategories = await database.Categories
        .Where(category => category.HouseholdId == householdId)
        .OrderBy(category => category.SortOrder)
        .ToListAsync();
    var category = householdCategories.SingleOrDefault(value => value.Id == id);
    if (category is null) return Results.NotFound();
    if (householdCategories.Count == 1)
    {
        return Results.BadRequest(new { message = "A household needs at least one category." });
    }

    var fallback = householdCategories.FirstOrDefault(value =>
        value.Id != id && string.Equals(value.Name, "Other", StringComparison.OrdinalIgnoreCase))
        ?? householdCategories.First(value => value.Id != id);

    await using var transaction = await database.Database.BeginTransactionAsync();
    await database.GroceryItems
        .Where(item => item.HouseholdId == householdId && item.Category == category.Name)
        .ExecuteUpdateAsync(update => update.SetProperty(item => item.Category, fallback.Name));
    database.Categories.Remove(category);
    await database.SaveChangesAsync();
    await transaction.CommitAsync();
    return Results.Ok(new { reassignedTo = fallback.Name });
});

var groceries = app.MapGroup("/api/groceries").RequireAuthorization();

groceries.MapGet("/", async (ClaimsPrincipal principal, AppDbContext database) =>
{
    var householdId = await GetHouseholdIdAsync(principal, database);
    var items = await database.GroceryItems
        .AsNoTracking()
        .Where(item => item.HouseholdId == householdId)
        .Select(item => new GroceryItemResponse(
            item.Id,
            item.Name,
            item.Quantity,
            item.Unit,
            item.Category,
            item.IsCompleted,
            item.CreatedAt))
        .ToListAsync();
    return Results.Ok(items
        .OrderBy(item => item.IsCompleted)
        .ThenBy(item => item.CreatedAt));
});

groceries.MapPost("/", async (
    GroceryItemInput input,
    ClaimsPrincipal principal,
    AppDbContext database) =>
{
    var error = ValidateGrocery(input);
    if (error is not null) return Results.BadRequest(new { message = error });

    var householdId = await GetHouseholdIdAsync(principal, database);
    var categoryExists = await database.Categories.AnyAsync(category =>
        category.HouseholdId == householdId && category.Name == input.Category);
    if (!categoryExists) return Results.BadRequest(new { message = "Choose a category from this household." });

    var item = new GroceryItem
    {
        Id = Guid.NewGuid(),
        Name = input.Name.Trim(),
        Quantity = input.Quantity,
        Unit = input.Unit.Trim(),
        Category = input.Category,
        IsCompleted = input.IsCompleted,
        CreatedAt = DateTimeOffset.UtcNow,
        HouseholdId = householdId,
        CreatedByUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
    };
    database.GroceryItems.Add(item);
    await database.SaveChangesAsync();
    return Results.Created($"/api/groceries/{item.Id}", ToResponse(item));
});

groceries.MapPut("/{id:guid}", async (
    Guid id,
    GroceryItemInput input,
    ClaimsPrincipal principal,
    AppDbContext database) =>
{
    var error = ValidateGrocery(input);
    if (error is not null) return Results.BadRequest(new { message = error });

    var householdId = await GetHouseholdIdAsync(principal, database);
    var categoryExists = await database.Categories.AnyAsync(category =>
        category.HouseholdId == householdId && category.Name == input.Category);
    if (!categoryExists) return Results.BadRequest(new { message = "Choose a category from this household." });
    var item = await database.GroceryItems.SingleOrDefaultAsync(value => value.Id == id && value.HouseholdId == householdId);
    if (item is null) return Results.NotFound();

    item.Name = input.Name.Trim();
    item.Quantity = input.Quantity;
    item.Unit = input.Unit.Trim();
    item.Category = input.Category;
    item.IsCompleted = input.IsCompleted;
    await database.SaveChangesAsync();
    return Results.Ok(ToResponse(item));
});

groceries.MapDelete("/{id:guid}", async (
    Guid id,
    ClaimsPrincipal principal,
    AppDbContext database) =>
{
    var householdId = await GetHouseholdIdAsync(principal, database);
    var item = await database.GroceryItems.SingleOrDefaultAsync(value => value.Id == id && value.HouseholdId == householdId);
    if (item is null) return Results.NotFound();

    database.GroceryItems.Remove(item);
    await database.SaveChangesAsync();
    return Results.NoContent();
});

groceries.MapDelete("/completed", async (ClaimsPrincipal principal, AppDbContext database) =>
{
    var householdId = await GetHouseholdIdAsync(principal, database);
    var removed = await database.GroceryItems
        .Where(item => item.HouseholdId == householdId && item.IsCompleted)
        .ExecuteDeleteAsync();
    return Results.Ok(new { removed });
});

app.MapFallbackToFile("index.html");
app.Run();

static async Task<Guid> GetHouseholdIdAsync(ClaimsPrincipal principal, AppDbContext database)
{
    var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
    return await database.Users
        .Where(user => user.Id == userId)
        .Select(user => user.HouseholdId)
        .SingleAsync();
}

static async Task<SessionResponse> BuildSessionAsync(string userId, AppDbContext database)
{
    return await database.Users
        .Where(user => user.Id == userId)
        .Select(user => new SessionResponse(
            new UserResponse(user.Id, user.DisplayName, user.Email!),
            new HouseholdResponse(
                user.Household.Id,
                user.Household.Name,
                user.Household.InviteCode,
                user.Household.Members
                    .OrderBy(member => member.DisplayName)
                    .Select(member => new HouseholdMemberResponse(member.Id, member.DisplayName))
                    .ToArray())))
        .SingleAsync();
}

static async Task<string> CreateUniqueInviteCodeAsync(AppDbContext database)
{
    const string characters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    while (true)
    {
        var code = new string(RandomNumberGenerator.GetBytes(8)
            .Select(value => characters[value % characters.Length])
            .ToArray());
        if (!await database.Households.AnyAsync(household => household.InviteCode == code)) return code;
    }
}

static List<HouseholdCategory> CreateDefaultCategories(Guid householdId)
{
    var defaults = new (string Name, string Icon)[]
    {
        ("Produce", "🥬"),
        ("Dairy", "🥛"),
        ("Bakery", "🥖"),
        ("Meat", "🥩"),
        ("Pantry", "🫙"),
        ("Frozen", "❄️"),
        ("Household", "🧺"),
        ("Other", "🛒")
    };
    return defaults.Select((value, index) => new HouseholdCategory
    {
        Id = Guid.NewGuid(),
        Name = value.Name,
        Icon = value.Icon,
        SortOrder = index,
        HouseholdId = householdId
    }).ToList();
}

static string? ValidateRegistration(RegisterRequest request)
{
    if (string.IsNullOrWhiteSpace(request.DisplayName)) return "Enter your name.";
    if (request.DisplayName.Trim().Length > 60) return "Keep your name under 60 characters.";
    if (string.IsNullOrWhiteSpace(request.Email)) return "Enter your email address.";
    if (request.Password.Length < 8) return "Use a password with at least 8 characters.";
    if (request.HouseholdName.Trim().Length > 80) return "Keep the household name under 80 characters.";
    return null;
}

static string? ValidateGrocery(GroceryItemInput input)
{
    if (string.IsNullOrWhiteSpace(input.Name)) return "Enter an item name.";
    if (input.Name.Trim().Length > 80) return "Keep the item name under 80 characters.";
    if (input.Quantity is < 1 or > 999) return "Quantity must be between 1 and 999.";
    if (input.Unit.Trim().Length > 24) return "Keep the unit under 24 characters.";
    return null;
}

static string? ValidateCategory(CategoryInput input)
{
    if (string.IsNullOrWhiteSpace(input.Name)) return "Enter a category name.";
    if (input.Name.Trim().Length > 32) return "Keep the category name under 32 characters.";
    if (string.IsNullOrWhiteSpace(input.Icon)) return "Choose an icon for the category.";
    if (input.Icon.Trim().Length > 16) return "Keep the category icon short.";
    return null;
}

static GroceryItemResponse ToResponse(GroceryItem item) => new(
    item.Id,
    item.Name,
    item.Quantity,
    item.Unit,
    item.Category,
    item.IsCompleted,
    item.CreatedAt);

static CategoryResponse ToCategoryResponse(HouseholdCategory category) => new(
    category.Id,
    category.Name,
    category.Icon,
    category.SortOrder);

public sealed record RegisterRequest(string DisplayName, string Email, string Password, string HouseholdName = "");
public sealed record LoginRequest(string Email, string Password, bool RememberMe = true);
public sealed record JoinHouseholdRequest(string InviteCode);
public sealed record UserResponse(string Id, string DisplayName, string Email);
public sealed record HouseholdMemberResponse(string Id, string DisplayName);
public sealed record HouseholdResponse(Guid Id, string Name, string InviteCode, HouseholdMemberResponse[] Members);
public sealed record SessionResponse(UserResponse User, HouseholdResponse Household);
public sealed record CategoryInput(string Name, string Icon);
public sealed record CategoryResponse(Guid Id, string Name, string Icon, int SortOrder);
public sealed record GroceryItemInput(
    string Name,
    int Quantity = 1,
    string Unit = "",
    string Category = "Other",
    bool IsCompleted = false);
public sealed record GroceryItemResponse(
    Guid Id,
    string Name,
    int Quantity,
    string Unit,
    string Category,
    bool IsCompleted,
    DateTimeOffset CreatedAt);

using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.Sqlite;
using Quarry.Sample.WebApp.Auth;
using Quarry.Sample.WebApp.Data;
using Quarry.Sample.WebApp.Logging;
using Quarry.Sample.WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Data ---
builder.Services.AddScoped<SqliteConnection>(sp =>
    new SqliteConnection(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<AppDb>(sp =>
    new AppDb(sp.GetRequiredService<SqliteConnection>()));

// --- Services ---
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<SessionService>();

// --- Auth ---
builder.Services.AddAuthentication(SessionAuthDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthHandler>(
        SessionAuthDefaults.Scheme, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireClaim(
        System.Security.Claims.ClaimTypes.Role, "Admin"));
});

builder.Services.AddRazorPages();

var app = builder.Build();

// --- Logsmith bridge ---
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
Quarry.Logging.LogsmithOutput.Logger = new LogsmithBridge(loggerFactory);

// --- Database initialization ---
using (var scope = app.Services.CreateScope())
{
    var connection = scope.ServiceProvider.GetRequiredService<SqliteConnection>();
    await connection.OpenAsync();

    await using var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.MigrateAsync(connection);

    var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
    await SeedData.InitializeAsync(db, hasher);
}

// --- Pipeline ---
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();

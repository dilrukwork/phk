using AzureResourceLister;
using AzureResourceLister.Data;
using AzureResourceLister.Llm;
using AzureResourceLister.Models;
using AzureResourceLister.Web.Components;
using Microsoft.EntityFrameworkCore;
using Serilog;

// ── Serilog bootstrap ─────────────────────────────────────────────────────────
// Configured before the host builder so startup errors are also captured.
// Logs roll daily under  <app_dir>/logs/  with 30 days retention.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.File(
        path: "logs/app-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting AzureResourceLister");

    var builder = WebApplication.CreateBuilder(args);

    // Replace .NET's default logging with Serilog
    builder.Host.UseSerilog();

    // ── Razor Components (Blazor) — Interactive Server, applied globally ─────────
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // ── App configuration ──────────────────────────────────────────────────────
    var appConfig = AppConfig.FromConfiguration(builder.Configuration);
    builder.Services.AddSingleton(appConfig);

    // ── Azure + AI services ────────────────────────────────────────────────────
    builder.Services.AddSingleton<AzureTokenService>();
    builder.Services.AddSingleton<AzureResourceService>();
    builder.Services.AddSingleton<ILlmProvider>(_ =>
    {
        if (appConfig.AiProvider.Equals("AzureFoundry", StringComparison.OrdinalIgnoreCase))
            return new AzureFoundryLlmProvider(appConfig.AzureFoundryEndpoint, appConfig.AzureFoundryApiKey, appConfig.AzureFoundryDeploymentName);

        if (appConfig.AiProvider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
            return new AnthropicLlmProvider(appConfig.AnthropicApiKey, appConfig.AnthropicModel);

        return new OllamaLlmProvider(appConfig.OllamaEndpoint, appConfig.OllamaModel);
    });

    builder.Services.AddScoped<ApplicationAdminService>();
    builder.Services.AddScoped<ApplicationImportService>();
    builder.Services.AddScoped<OwnerAdminService>();
    builder.Services.AddScoped<OwnerImportService>();
    builder.Services.AddScoped<ResourceAdminService>();
    builder.Services.AddScoped<ResourceImportService>();
    builder.Services.AddScoped<MeterAdminService>();
    builder.Services.AddScoped<BillingImportService>();
    builder.Services.AddScoped<CostExceptionAdminService>();
    builder.Services.AddScoped<SharedAppImportService>();
    builder.Services.AddScoped<SharedAppExceptionAdminService>();
    builder.Services.AddScoped<ReportService>();
    builder.Services.AddScoped<LicenceFindingService>();
    builder.Services.AddScoped<UnattachedDiskFindingService>();
    builder.Services.AddScoped<PriceSheetImportService>();
    builder.Services.AddScoped<SavingsPlanAdminService>();
    builder.Services.AddScoped<ReservedInstanceImportService>();
    builder.Services.AddScoped<ReservedInstanceAdminService>();
    builder.Services.AddScoped<SubscriptionLookupService>();

    // ── EF Core ────────────────────────────────────────────────────────────────
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

    builder.Services.AddDbContextFactory<AppDbContext>(opt =>
        opt.UseSqlServer(connectionString));

    var app = builder.Build();

    // ── Apply any pending migrations on startup ───────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = await dbContextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await EnsureUnassignedPlaceholdersAsync(db);
    }

    // ── Middleware pipeline ────────────────────────────────────────────────────
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "AzureResourceLister terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// ── Local functions ──────────────────────────────────────────────────────────

static async Task EnsureUnassignedPlaceholdersAsync(AppDbContext db)
{
    const string Unassigned = "UNASSIGNED";

    if (!await db.Applications.AnyAsync(a => a.ApplicationName.ToUpper() == Unassigned))
        db.Applications.Add(new Application { ApplicationName = Unassigned, Budget = 0m });

    if (!await db.BusinessOwners.AnyAsync(o => o.OwnerName.ToUpper() == Unassigned))
        db.BusinessOwners.Add(new BusinessOwner { OwnerName = Unassigned });

    await db.SaveChangesAsync();
}

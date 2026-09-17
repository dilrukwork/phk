using AzureResourceLister.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Application>           Applications          { get; set; }
    public DbSet<BusinessOwner>         BusinessOwners        { get; set; }
    public DbSet<AzureEnvironment>      AzureEnvironments     { get; set; }
    public DbSet<Subscription>          Subscriptions         { get; set; }
    public DbSet<Resource>              Resources             { get; set; }
    public DbSet<ResourceApp>           ResourceApps          { get; set; }
    public DbSet<ResourceOwner>         ResourceOwners        { get; set; }
    public DbSet<Meter>                 Meters                { get; set; }
    public DbSet<ResourceCost>          ResourceCosts         { get; set; }
    public DbSet<ResourceCostException> ResourceCostExceptions { get; set; }
    public DbSet<MergeProposal>            MergeProposals            { get; set; }
    public DbSet<RawValueMapping>          RawValueMappings          { get; set; }
    public DbSet<SharedAppImportException> SharedAppImportExceptions { get; set; }
    public DbSet<PriceSheetEntry>          PriceSheetEntries         { get; set; }
    public DbSet<ReservedInstancePriceEntry> ReservedInstancePriceEntries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Application ───────────────────────────────────────────────────────
        modelBuilder.Entity<Application>(e =>
        {
            e.ToTable("Application");
            e.HasKey(a => a.Id);

            e.Property(a => a.ApplicationName)
                .IsRequired()
                .HasMaxLength(256);

            e.Property(a => a.Budget)
                .HasColumnType("money");
        });

        // ── BusinessOwner ─────────────────────────────────────────────────────
        modelBuilder.Entity<BusinessOwner>(e =>
        {
            e.ToTable("BusinessOwner");
            e.HasKey(b => b.Id);

            e.Property(b => b.OwnerName)
                .IsRequired()
                .HasMaxLength(256);
        });

        // ── AzureEnvironment ──────────────────────────────────────────────────
        modelBuilder.Entity<AzureEnvironment>(e =>
        {
            // Map to "Environment" table to match the diagram name
            e.ToTable("Environment");
            e.HasKey(env => env.Id);

            e.Property(env => env.EnvironmentName)
                .IsRequired()
                .HasMaxLength(128);
        });

        // ── Subscription ──────────────────────────────────────────────────────
        modelBuilder.Entity<Subscription>(e =>
        {
            e.ToTable("Subscription");
            e.HasKey(s => s.Id);

            e.Property(s => s.SubscriptionName)
                .IsRequired()
                .HasMaxLength(256);

            e.Property(s => s.AzureSubscriptionId)
                .IsRequired()
                .HasMaxLength(36);   // standard GUID length

            e.HasIndex(s => s.AzureSubscriptionId)
                .IsUnique()
                .HasDatabaseName("UX_Subscription_AzureSubscriptionId");

            e.HasOne(s => s.Environment)
                .WithMany(env => env.Subscriptions)
                .HasForeignKey(s => s.EnvironmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Resource ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Resource>(e =>
        {
            e.ToTable("Resource");
            e.HasKey(r => r.Id);

            e.Property(r => r.AzureResourceId)
                .IsRequired()
                .HasMaxLength(1024);

            e.Property(r => r.ResourceName)
                .IsRequired()
                .HasMaxLength(512);

            e.Property(r => r.ResourceGroup)
                .HasMaxLength(512)
                .HasDefaultValue(string.Empty);

            e.Property(r => r.CostCentre)
                .HasMaxLength(30)
                .HasDefaultValue(string.Empty);

            e.Property(r => r.HoursOfOperation)
                .HasMaxLength(10)
                .HasDefaultValue("24x7");

            // IsActive stored as a bit column named "Status" to match the diagram
            e.Property(r => r.IsActive)
                .HasColumnName("Status");

            e.Property(r => r.ExcludeFromSavingsPlan)
                .HasDefaultValue(false);

            e.Property(r => r.Source)
                .HasMaxLength(32)
                .HasDefaultValue("AzureSync");

            e.HasOne(r => r.Subscription)
                .WithMany(s => s.Resources)
                .HasForeignKey(r => r.SubscriptionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── ResourceApp ───────────────────────────────────────────────────────
        // Many-to-many: Resource <-> Application. A dedicated resource has one row
        // at Weight = 1.0; a shared resource has one row per consuming application.
        modelBuilder.Entity<ResourceApp>(e =>
        {
            e.ToTable("ResourceApp");
            e.HasKey(ra => ra.Id);

            e.Property(ra => ra.Weight)
                .HasColumnType("decimal(5,4)")
                .HasDefaultValue(1.0m);

            e.Property(ra => ra.Source)
                .HasMaxLength(32)
                .HasDefaultValue("TagSync");

            e.HasOne(ra => ra.Resource)
                .WithMany(r => r.ResourceApps)
                .HasForeignKey(ra => ra.ResourceId)
                .OnDelete(DeleteBehavior.Cascade); // junction row is meaningless without the resource

            e.HasOne(ra => ra.Application)
                .WithMany()
                .HasForeignKey(ra => ra.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict); // matches the existing "can't delete a referenced Application" rule

            e.HasIndex(ra => new { ra.ResourceId, ra.ApplicationId, ra.Source })
                .IsUnique()
                .HasDatabaseName("UX_ResourceApp_ResourceId_ApplicationId_Source");
        });

        // ── ResourceOwner ─────────────────────────────────────────────────────
        // Many-to-many: Resource <-> BusinessOwner. Modelled the same way as
        // ResourceApp for consistency, even though today it's always one row per resource.
        modelBuilder.Entity<ResourceOwner>(e =>
        {
            e.ToTable("ResourceOwner");
            e.HasKey(ro => ro.Id);

            e.Property(ro => ro.Source)
                .HasMaxLength(32)
                .HasDefaultValue("TagSync");

            e.HasOne(ro => ro.Resource)
                .WithMany(r => r.ResourceOwners)
                .HasForeignKey(ro => ro.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ro => ro.Owner)
                .WithMany()
                .HasForeignKey(ro => ro.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(ro => new { ro.ResourceId, ro.OwnerId, ro.Source })
                .IsUnique()
                .HasDatabaseName("UX_ResourceOwner_ResourceId_OwnerId_Source");
        });

        // ── Meter ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Meter>(e =>
        {
            e.ToTable("Meter");
            e.HasKey(m => m.Id);

            e.Property(m => m.MeterCategory)
                .IsRequired()
                .HasMaxLength(256);

            e.Property(m => m.MeterSubCategory)
                .HasMaxLength(256)
                .HasDefaultValue(string.Empty);

            e.Property(m => m.MeterName)
                .IsRequired()
                .HasMaxLength(256);

            e.Property(m => m.UnitOfMeasure)
                .HasMaxLength(64)
                .HasDefaultValue(string.Empty);

            // Composite unique index — a meter is uniquely identified by all four fields
            e.HasIndex(m => new { m.MeterCategory, m.MeterSubCategory, m.MeterName, m.UnitOfMeasure })
                .IsUnique()
                .HasDatabaseName("UX_Meter_Composite");
        });

        // ── ResourceCost ──────────────────────────────────────────────────────
        modelBuilder.Entity<ResourceCost>(e =>
        {
            e.ToTable("ResourceCost");
            e.HasKey(rc => rc.Id);

            e.Property(rc => rc.AzureResourceId)
                .IsRequired()
                .HasMaxLength(1024);

            e.Property(rc => rc.Cost)          .HasColumnType("decimal(18,10)");
            e.Property(rc => rc.Quantity)      .HasColumnType("decimal(18,10)");
            e.Property(rc => rc.EffectivePrice).HasColumnType("decimal(18,10)");
            e.Property(rc => rc.UnitPrice)     .HasColumnType("decimal(18,10)");
            e.Property(rc => rc.PAYGPrice)     .HasColumnType("decimal(18,10)");

            e.Property(rc => rc.OfferId)      .HasMaxLength(128).HasDefaultValue(string.Empty);
            e.Property(rc => rc.PricingModel) .HasMaxLength(64) .HasDefaultValue(string.Empty);

            e.HasOne(rc => rc.Resource)
                .WithMany()
                .HasForeignKey(rc => rc.ResourceDbId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(rc => rc.Meter)
                .WithMany(m => m.ResourceCosts)
                .HasForeignKey(rc => rc.MeterId)
                .OnDelete(DeleteBehavior.Restrict);

            // Index for fast lookups by resource
            e.HasIndex(rc => rc.AzureResourceId)
                .HasDatabaseName("IX_ResourceCost_AzureResourceId");

            e.HasIndex(rc => rc.BillingPeriodStart)
                .HasDatabaseName("IX_ResourceCost_BillingPeriodStart");
        });

        // ── ResourceCostException ─────────────────────────────────────────────
        modelBuilder.Entity<ResourceCostException>(e =>
        {
            e.ToTable("ResourceCostException");
            e.HasKey(ex => ex.Id);

            e.Property(ex => ex.AzureResourceId)  .IsRequired().HasMaxLength(1024);
            e.Property(ex => ex.SourceFile)        .HasMaxLength(512).HasDefaultValue(string.Empty);
            e.Property(ex => ex.MeterCategory)     .HasMaxLength(256).HasDefaultValue(string.Empty);
            e.Property(ex => ex.MeterSubCategory)  .HasMaxLength(256).HasDefaultValue(string.Empty);
            e.Property(ex => ex.MeterName)         .HasMaxLength(256).HasDefaultValue(string.Empty);
            e.Property(ex => ex.UnitOfMeasure)     .HasMaxLength(64) .HasDefaultValue(string.Empty);
            e.Property(ex => ex.OfferId)           .HasMaxLength(128).HasDefaultValue(string.Empty);
            e.Property(ex => ex.PricingModel)      .HasMaxLength(64) .HasDefaultValue(string.Empty);

            e.Property(ex => ex.Cost)          .HasColumnType("decimal(18,10)");
            e.Property(ex => ex.Quantity)      .HasColumnType("decimal(18,10)");
            e.Property(ex => ex.EffectivePrice).HasColumnType("decimal(18,10)");
            e.Property(ex => ex.UnitPrice)     .HasColumnType("decimal(18,10)");
            e.Property(ex => ex.PAYGPrice)     .HasColumnType("decimal(18,10)");

            e.HasOne(ex => ex.Meter)
                .WithMany()
                .HasForeignKey(ex => ex.MeterId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(ex => ex.AzureResourceId)
                .HasDatabaseName("IX_ResourceCostException_AzureResourceId");
        });

        // ── MergeProposal ─────────────────────────────────────────────────────
        // No FK constraints — SourceId/TargetId point at either Application or
        // BusinessOwner depending on EntityType, so a single FK isn't possible.
        modelBuilder.Entity<MergeProposal>(e =>
        {
            e.ToTable("MergeProposal");
            e.HasKey(m => m.Id);

            e.Property(m => m.EntityType) .IsRequired().HasMaxLength(32);
            e.Property(m => m.SourceName) .IsRequired().HasMaxLength(256);
            e.Property(m => m.TargetName) .IsRequired().HasMaxLength(256);
            e.Property(m => m.Confidence) .HasMaxLength(16).HasDefaultValue(string.Empty);
            e.Property(m => m.Reasoning)  .HasMaxLength(1024).HasDefaultValue(string.Empty);

            e.HasIndex(m => new { m.EntityType, m.IsApplied })
                .HasDatabaseName("IX_MergeProposal_EntityType_IsApplied");
        });

        // ── RawValueMapping ───────────────────────────────────────────────────
        modelBuilder.Entity<RawValueMapping>(e =>
        {
            e.ToTable("RawValueMapping");
            e.HasKey(m => m.Id);

            e.Property(m => m.EntityType)    .IsRequired().HasMaxLength(32);
            e.Property(m => m.RawValue)      .IsRequired().HasMaxLength(512);
            e.Property(m => m.CanonicalName) .IsRequired().HasMaxLength(256);

            e.HasIndex(m => new { m.EntityType, m.RawValue })
                .IsUnique()
                .HasDatabaseName("UX_RawValueMapping_EntityType_RawValue");
        });

        // ── SharedAppImportException ──────────────────────────────────────────
        modelBuilder.Entity<SharedAppImportException>(e =>
        {
            e.ToTable("SharedAppImportException");
            e.HasKey(ex => ex.Id);

            e.Property(ex => ex.ApplicationName)   .IsRequired().HasMaxLength(256);
            e.Property(ex => ex.OwnerName)         .HasMaxLength(256).HasDefaultValue(string.Empty);
            e.Property(ex => ex.AzureResourceName) .IsRequired().HasMaxLength(512);
            e.Property(ex => ex.SubscriptionName)  .HasMaxLength(256).HasDefaultValue(string.Empty);
            e.Property(ex => ex.ResourceGroup)     .HasMaxLength(512).HasDefaultValue(string.Empty);
            e.Property(ex => ex.Reason)            .HasMaxLength(256).HasDefaultValue(string.Empty);
            e.Property(ex => ex.SourceFile)        .HasMaxLength(512).HasDefaultValue(string.Empty);
        });

        // ── PriceSheetEntry ───────────────────────────────────────────────────
        modelBuilder.Entity<PriceSheetEntry>(e =>
        {
            e.ToTable("PriceSheetEntry");
            e.HasKey(p => p.Id);

            e.Property(p => p.AzureMeterId)     .HasMaxLength(36) .HasDefaultValue(string.Empty);
            e.Property(p => p.MeterCategory)    .IsRequired().HasMaxLength(256);
            e.Property(p => p.MeterSubCategory) .HasMaxLength(256).HasDefaultValue(string.Empty);
            e.Property(p => p.MeterName)        .IsRequired().HasMaxLength(256);
            e.Property(p => p.MeterRegion)      .HasMaxLength(128).HasDefaultValue(string.Empty);
            e.Property(p => p.Term)             .HasMaxLength(10) .HasDefaultValue(string.Empty);
            e.Property(p => p.UnitPrice)        .HasColumnType("decimal(18,6)");
            e.Property(p => p.UnitOfMeasure)    .HasMaxLength(64) .HasDefaultValue(string.Empty);
            e.Property(p => p.OfferId)          .HasMaxLength(64) .HasDefaultValue(string.Empty);
            e.Property(p => p.Product)          .HasMaxLength(256).HasDefaultValue(string.Empty);

            e.HasIndex(p => new { p.MeterCategory, p.MeterSubCategory, p.MeterName, p.Term })
             .HasDatabaseName("IX_PriceSheetEntry_MeterKey_Term");
        });

        // ── ReservedInstancePriceEntry ────────────────────────────────────────
        modelBuilder.Entity<ReservedInstancePriceEntry>(e =>
        {
            e.ToTable("ReservedInstancePriceEntry");
            e.HasKey(p => p.Id);

            e.Property(p => p.AzureMeterId)     .HasMaxLength(36) .HasDefaultValue(string.Empty);
            e.Property(p => p.MeterCategory)    .IsRequired().HasMaxLength(256);
            e.Property(p => p.MeterSubCategory) .HasMaxLength(256).HasDefaultValue(string.Empty);
            e.Property(p => p.MeterName)        .IsRequired().HasMaxLength(256);
            e.Property(p => p.MeterRegion)      .HasMaxLength(128).HasDefaultValue(string.Empty);
            e.Property(p => p.Term)             .HasMaxLength(10) .HasDefaultValue(string.Empty);
            e.Property(p => p.UnitPrice)        .HasColumnType("decimal(18,6)");
            e.Property(p => p.UnitOfMeasure)    .HasMaxLength(64) .HasDefaultValue(string.Empty);
            e.Property(p => p.OfferId)          .HasMaxLength(64) .HasDefaultValue(string.Empty);
            e.Property(p => p.Product)          .HasMaxLength(256).HasDefaultValue(string.Empty);

            e.HasIndex(p => new { p.MeterCategory, p.MeterSubCategory, p.MeterName, p.Term })
             .HasDatabaseName("IX_ReservedInstancePriceEntry_MeterKey_Term");
        });
    }
}

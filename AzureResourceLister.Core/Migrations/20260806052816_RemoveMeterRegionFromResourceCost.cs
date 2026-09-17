using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureResourceLister.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMeterRegionFromResourceCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Application",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Budget = table.Column<decimal>(type: "money", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Application", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessOwner",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessOwner", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Environment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EnvironmentName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Environment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MergeProposal",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    TargetName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Confidence = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: ""),
                    Reasoning = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false, defaultValue: ""),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsApplied = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MergeProposal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Meter",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MeterCategory = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MeterSubCategory = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    MeterName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meter", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PriceSheetEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AzureMeterId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false, defaultValue: ""),
                    MeterCategory = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MeterSubCategory = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    MeterName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MeterRegion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, defaultValue: ""),
                    Term = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: ""),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    OfferId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    Product = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    EffectiveStartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveEndDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceSheetEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawValueMapping",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RawValue = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CanonicalName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawValueMapping", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReservedInstancePriceEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AzureMeterId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false, defaultValue: ""),
                    MeterCategory = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MeterSubCategory = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    MeterName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MeterRegion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, defaultValue: ""),
                    Term = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: ""),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    OfferId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    Product = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    EffectiveStartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveEndDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservedInstancePriceEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SharedAppImportException",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    AzureResourceName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SubscriptionName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    ResourceGroup = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, defaultValue: ""),
                    Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    SourceFile = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, defaultValue: ""),
                    LoggedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedAppImportException", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscription",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscriptionName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AzureSubscriptionId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    EnvironmentId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscription", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscription_Environment_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResourceCostException",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AzureResourceId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    SourceFile = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, defaultValue: ""),
                    MeterId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
                    EffectivePrice = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
                    PAYGPrice = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
                    MeterCategory = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    MeterSubCategory = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    MeterName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    UnitOfMeasure = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    OfferId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, defaultValue: ""),
                    PricingModel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    BillingPeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LoggedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceCostException", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceCostException_Meter_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Resource",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AzureResourceId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    SubscriptionId = table.Column<int>(type: "int", nullable: false),
                    ResourceName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ResourceGroup = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, defaultValue: ""),
                    CostCentre = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: ""),
                    HoursOfOperation = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "24x7"),
                    Status = table.Column<bool>(type: "bit", nullable: false),
                    ExcludeFromSavingsPlan = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "AzureSync"),
                    ApplicationId = table.Column<int>(type: "int", nullable: true),
                    BusinessOwnerId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resource", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Resource_Application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Application",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Resource_BusinessOwner_BusinessOwnerId",
                        column: x => x.BusinessOwnerId,
                        principalTable: "BusinessOwner",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Resource_Subscription_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscription",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResourceApp",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(5,4)", nullable: false, defaultValue: 1.0m),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "TagSync")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceApp", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceApp_Application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceApp_Resource_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceCost",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AzureResourceId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    ResourceDbId = table.Column<int>(type: "int", nullable: true),
                    MeterId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
                    EffectivePrice = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
                    PAYGPrice = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
                    OfferId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, defaultValue: ""),
                    PricingModel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    BillingPeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceCost", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceCost_Meter_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceCost_Resource_ResourceDbId",
                        column: x => x.ResourceDbId,
                        principalTable: "Resource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResourceOwner",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "TagSync")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceOwner", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceOwner_BusinessOwner_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "BusinessOwner",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceOwner_Resource_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MergeProposal_EntityType_IsApplied",
                table: "MergeProposal",
                columns: new[] { "EntityType", "IsApplied" });

            migrationBuilder.CreateIndex(
                name: "UX_Meter_Composite",
                table: "Meter",
                columns: new[] { "MeterCategory", "MeterSubCategory", "MeterName", "UnitOfMeasure" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceSheetEntry_MeterKey_Term",
                table: "PriceSheetEntry",
                columns: new[] { "MeterCategory", "MeterSubCategory", "MeterName", "Term" });

            migrationBuilder.CreateIndex(
                name: "UX_RawValueMapping_EntityType_RawValue",
                table: "RawValueMapping",
                columns: new[] { "EntityType", "RawValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReservedInstancePriceEntry_MeterKey_Term",
                table: "ReservedInstancePriceEntry",
                columns: new[] { "MeterCategory", "MeterSubCategory", "MeterName", "Term" });

            migrationBuilder.CreateIndex(
                name: "IX_Resource_ApplicationId",
                table: "Resource",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Resource_BusinessOwnerId",
                table: "Resource",
                column: "BusinessOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Resource_SubscriptionId",
                table: "Resource",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceApp_ApplicationId",
                table: "ResourceApp",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "UX_ResourceApp_ResourceId_ApplicationId_Source",
                table: "ResourceApp",
                columns: new[] { "ResourceId", "ApplicationId", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceCost_AzureResourceId",
                table: "ResourceCost",
                column: "AzureResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceCost_BillingPeriodStart",
                table: "ResourceCost",
                column: "BillingPeriodStart");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceCost_MeterId",
                table: "ResourceCost",
                column: "MeterId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceCost_ResourceDbId",
                table: "ResourceCost",
                column: "ResourceDbId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceCostException_AzureResourceId",
                table: "ResourceCostException",
                column: "AzureResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceCostException_MeterId",
                table: "ResourceCostException",
                column: "MeterId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceOwner_OwnerId",
                table: "ResourceOwner",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "UX_ResourceOwner_ResourceId_OwnerId_Source",
                table: "ResourceOwner",
                columns: new[] { "ResourceId", "OwnerId", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscription_EnvironmentId",
                table: "Subscription",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "UX_Subscription_AzureSubscriptionId",
                table: "Subscription",
                column: "AzureSubscriptionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MergeProposal");

            migrationBuilder.DropTable(
                name: "PriceSheetEntry");

            migrationBuilder.DropTable(
                name: "RawValueMapping");

            migrationBuilder.DropTable(
                name: "ReservedInstancePriceEntry");

            migrationBuilder.DropTable(
                name: "ResourceApp");

            migrationBuilder.DropTable(
                name: "ResourceCost");

            migrationBuilder.DropTable(
                name: "ResourceCostException");

            migrationBuilder.DropTable(
                name: "ResourceOwner");

            migrationBuilder.DropTable(
                name: "SharedAppImportException");

            migrationBuilder.DropTable(
                name: "Meter");

            migrationBuilder.DropTable(
                name: "Resource");

            migrationBuilder.DropTable(
                name: "Application");

            migrationBuilder.DropTable(
                name: "BusinessOwner");

            migrationBuilder.DropTable(
                name: "Subscription");

            migrationBuilder.DropTable(
                name: "Environment");
        }
    }
}

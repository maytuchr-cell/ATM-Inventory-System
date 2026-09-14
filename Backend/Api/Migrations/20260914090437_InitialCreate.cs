using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AtmModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ModelCode = table.Column<string>(type: "longtext", nullable: false),
                    ModelName = table.Column<string>(type: "longtext", nullable: false),
                    Manufacturer = table.Column<string>(type: "longtext", nullable: true),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtmModels", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EntityType = table.Column<string>(type: "varchar(255)", nullable: false),
                    EntityId = table.Column<string>(type: "varchar(255)", nullable: false),
                    Action = table.Column<string>(type: "longtext", nullable: false),
                    OldValues = table.Column<string>(type: "longtext", nullable: true),
                    NewValues = table.Column<string>(type: "longtext", nullable: true),
                    UserId = table.Column<string>(type: "longtext", nullable: false),
                    UserName = table.Column<string>(type: "longtext", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DailyReportImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    FileName = table.Column<string>(type: "longtext", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ImportedBy = table.Column<string>(type: "longtext", nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ReturnConfirmedCount = table.Column<int>(type: "int", nullable: false),
                    RepairCompletedCount = table.Column<int>(type: "int", nullable: false),
                    StillInRepairCount = table.Column<int>(type: "int", nullable: false),
                    UnmatchedCount = table.Column<int>(type: "int", nullable: false),
                    OutboundCount = table.Column<int>(type: "int", nullable: false),
                    InboundRepairedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReportImportBatches", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EquivalentGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquivalentGroups", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EquivalentParts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OriginalPartNo = table.Column<string>(type: "varchar(255)", nullable: false),
                    EquivalentPartNo = table.Column<string>(type: "varchar(255)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquivalentParts", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeContacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    FeId = table.Column<string>(type: "longtext", nullable: false),
                    FeName = table.Column<string>(type: "longtext", nullable: false),
                    Tel = table.Column<string>(type: "longtext", nullable: true),
                    Address = table.Column<string>(type: "longtext", nullable: false),
                    Postcode = table.Column<string>(type: "longtext", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeContacts", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    Code = table.Column<string>(type: "longtext", nullable: false),
                    LocationType = table.Column<string>(type: "longtext", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SavedAddresses",
                columns: table => new
                {
                    SavedAddressId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TechEmail = table.Column<string>(type: "longtext", nullable: false),
                    Label = table.Column<string>(type: "longtext", nullable: false),
                    Address = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedAddresses", x => x.SavedAddressId);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockCounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    CountType = table.Column<string>(type: "longtext", nullable: false),
                    Period = table.Column<string>(type: "longtext", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: false),
                    IsSystemFrozen = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StartedBy = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCounts", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    IsFrozen = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ActiveStockCountId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Tickets",
                columns: table => new
                {
                    TicketId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ExternalTicketNo = table.Column<string>(type: "varchar(255)", nullable: false),
                    TechEmail = table.Column<string>(type: "longtext", nullable: false),
                    TechName = table.Column<string>(type: "longtext", nullable: false),
                    TechDept = table.Column<string>(type: "longtext", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: true),
                    RejectReason = table.Column<string>(type: "longtext", nullable: true),
                    ApproverName = table.Column<string>(type: "longtext", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReturnAddress = table.Column<string>(type: "longtext", nullable: true),
                    ReturnEmailSentAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.TicketId);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Email = table.Column<string>(type: "varchar(255)", nullable: false),
                    PasswordHash = table.Column<string>(type: "longtext", nullable: false),
                    Role = table.Column<string>(type: "longtext", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    Code = table.Column<string>(type: "longtext", nullable: false),
                    VendorType = table.Column<string>(type: "longtext", nullable: false),
                    ContactInfo = table.Column<string>(type: "longtext", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Parts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OrderNumber = table.Column<string>(type: "longtext", nullable: false),
                    PartNo = table.Column<string>(type: "varchar(255)", nullable: false),
                    PartName = table.Column<string>(type: "longtext", nullable: false),
                    Unit = table.Column<string>(type: "longtext", nullable: false),
                    SerialNo = table.Column<string>(type: "longtext", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    CatalogueRef = table.Column<string>(type: "longtext", nullable: true),
                    MinStock = table.Column<int>(type: "int", nullable: false),
                    MaxStock = table.Column<int>(type: "int", nullable: false),
                    ReorderPoint = table.Column<int>(type: "int", nullable: false),
                    TrackingNumber = table.Column<string>(type: "longtext", nullable: true),
                    Aging = table.Column<int>(type: "int", nullable: true),
                    CostPerUnit = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsUnrepairable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MainUnit = table.Column<string>(type: "longtext", nullable: true),
                    Remark = table.Column<string>(type: "longtext", nullable: true),
                    ImagePath = table.Column<string>(type: "longtext", nullable: true),
                    Zone = table.Column<string>(type: "longtext", nullable: true),
                    DeviceType = table.Column<string>(type: "longtext", nullable: true),
                    AddedBy = table.Column<string>(type: "longtext", nullable: true),
                    Lot = table.Column<string>(type: "longtext", nullable: true),
                    Project = table.Column<string>(type: "longtext", nullable: true),
                    AddedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RowVersion = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Parts_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DailyReportImportRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    SourceSheet = table.Column<string>(type: "longtext", nullable: false),
                    RowIndex = table.Column<int>(type: "int", nullable: false),
                    PartNo = table.Column<string>(type: "longtext", nullable: false),
                    PartName = table.Column<string>(type: "longtext", nullable: false),
                    SerialNo = table.Column<string>(type: "longtext", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    DhlStatus = table.Column<string>(type: "longtext", nullable: false),
                    Problem = table.Column<string>(type: "longtext", nullable: true),
                    FeName = table.Column<string>(type: "longtext", nullable: true),
                    CaseNo = table.Column<string>(type: "longtext", nullable: true),
                    MatchType = table.Column<string>(type: "longtext", nullable: false),
                    TicketId = table.Column<int>(type: "int", nullable: true),
                    WithdrawBatchId = table.Column<int>(type: "int", nullable: true),
                    PartUnitId = table.Column<int>(type: "int", nullable: true),
                    StockCredited = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Undone = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UndoneAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReportImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyReportImportRows_DailyReportImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "DailyReportImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WithdrawBatches",
                columns: table => new
                {
                    WithdrawBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: true),
                    RejectReason = table.Column<string>(type: "longtext", nullable: true),
                    ApproverName = table.Column<string>(type: "longtext", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EmailSentAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    WithdrawAddress = table.Column<string>(type: "longtext", nullable: true),
                    WithdrawDescription = table.Column<string>(type: "longtext", nullable: true),
                    WithdrawSlipNo = table.Column<string>(type: "longtext", nullable: true),
                    WithdrawDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EmployeeCode = table.Column<string>(type: "longtext", nullable: true),
                    UsageStatus = table.Column<string>(type: "longtext", nullable: true),
                    TechSupportName = table.Column<string>(type: "longtext", nullable: true),
                    NeededByDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FeId = table.Column<string>(type: "longtext", nullable: true),
                    Sla = table.Column<string>(type: "longtext", nullable: true),
                    AtmCode = table.Column<string>(type: "longtext", nullable: true),
                    WaitingSinceAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReturnStatus = table.Column<string>(type: "longtext", nullable: true),
                    ReturnRejectReason = table.Column<string>(type: "longtext", nullable: true),
                    ReturnApproverName = table.Column<string>(type: "longtext", nullable: true),
                    ReturnApprovedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReturnAddress = table.Column<string>(type: "longtext", nullable: true),
                    ReturnEmailSentAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReturnSlipNo = table.Column<string>(type: "longtext", nullable: true),
                    ReturnRequestedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithdrawBatches", x => x.WithdrawBatchId);
                    table.ForeignKey(
                        name: "FK_WithdrawBatches_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "TicketId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GoodsReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ReceiptNo = table.Column<string>(type: "longtext", nullable: false),
                    Source = table.Column<string>(type: "longtext", nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: true),
                    RefDocument = table.Column<string>(type: "longtext", nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    ReceivedBy = table.Column<string>(type: "longtext", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    HandlingCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AtmModelParts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    AtmModelId = table.Column<int>(type: "int", nullable: false),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    PartNo = table.Column<string>(type: "varchar(255)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtmModelParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AtmModelParts_AtmModels_AtmModelId",
                        column: x => x.AtmModelId,
                        principalTable: "AtmModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AtmModelParts_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DisposalRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    PartNo = table.Column<string>(type: "longtext", nullable: false),
                    SerialNo = table.Column<string>(type: "longtext", nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: false),
                    ReasonCode = table.Column<string>(type: "longtext", nullable: false),
                    RequestedBy = table.Column<string>(type: "longtext", nullable: false),
                    ApprovedBy = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DisposedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisposalRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisposalRequests_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EquivalentGroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    PartNo = table.Column<string>(type: "varchar(255)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquivalentGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquivalentGroupMembers_EquivalentGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "EquivalentGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquivalentGroupMembers_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PartImages",
                columns: table => new
                {
                    PartImageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "longtext", nullable: false),
                    FileName = table.Column<string>(type: "longtext", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartImages", x => x.PartImageId);
                    table.ForeignKey(
                        name: "FK_PartImages_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PartStocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    GoodQty = table.Column<int>(type: "int", nullable: false),
                    BadQty = table.Column<int>(type: "int", nullable: false),
                    RepairQty = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RowVersion = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartStocks_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartStocks_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PartUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    SerialNo = table.Column<string>(type: "varchar(255)", nullable: false),
                    Condition = table.Column<string>(type: "longtext", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsUnrepairable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartUnits_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PartUnits_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReturnRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    PartNo = table.Column<string>(type: "longtext", nullable: false),
                    Condition = table.Column<string>(type: "longtext", nullable: false),
                    SourceType = table.Column<string>(type: "longtext", nullable: false),
                    LocationFromId = table.Column<int>(type: "int", nullable: false),
                    LocationToId = table.Column<int>(type: "int", nullable: false),
                    ReturnedBy = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "TicketId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockCountLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    StockCountId = table.Column<int>(type: "int", nullable: false),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    PartNo = table.Column<string>(type: "longtext", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    SystemQty = table.Column<int>(type: "int", nullable: false),
                    PhysicalQty = table.Column<int>(type: "int", nullable: true),
                    AdjustApproved = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Remarks = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCountLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCountLines_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountLines_StockCounts_StockCountId",
                        column: x => x.StockCountId,
                        principalTable: "StockCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    PartNo = table.Column<string>(type: "longtext", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    Condition = table.Column<string>(type: "longtext", nullable: false),
                    FromLocationId = table.Column<int>(type: "int", nullable: false),
                    ToLocationId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: false),
                    RequestedBy = table.Column<string>(type: "longtext", nullable: false),
                    ApprovedBy = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TicketAttachments",
                columns: table => new
                {
                    TicketAttachmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    WithdrawBatchId = table.Column<int>(type: "int", nullable: true),
                    Phase = table.Column<string>(type: "longtext", nullable: false),
                    FilePath = table.Column<string>(type: "longtext", nullable: false),
                    FileName = table.Column<string>(type: "longtext", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketAttachments", x => x.TicketAttachmentId);
                    table.ForeignKey(
                        name: "FK_TicketAttachments_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "TicketId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketAttachments_WithdrawBatches_WithdrawBatchId",
                        column: x => x.WithdrawBatchId,
                        principalTable: "WithdrawBatches",
                        principalColumn: "WithdrawBatchId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TicketPartLines",
                columns: table => new
                {
                    TicketPartLineId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    WithdrawBatchId = table.Column<int>(type: "int", nullable: true),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    PartNo = table.Column<string>(type: "longtext", nullable: false),
                    OriginalPartNo = table.Column<string>(type: "longtext", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    LineType = table.Column<string>(type: "longtext", nullable: false),
                    ConfirmedQty = table.Column<int>(type: "int", nullable: false),
                    Condition = table.Column<string>(type: "longtext", nullable: true),
                    Problem = table.Column<string>(type: "longtext", nullable: true),
                    SerialNo = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketPartLines", x => x.TicketPartLineId);
                    table.ForeignKey(
                        name: "FK_TicketPartLines_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketPartLines_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "TicketId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketPartLines_WithdrawBatches_WithdrawBatchId",
                        column: x => x.WithdrawBatchId,
                        principalTable: "WithdrawBatches",
                        principalColumn: "WithdrawBatchId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GoodsReceiptLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    GoodsReceiptId = table.Column<int>(type: "int", nullable: false),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    PartNo = table.Column<string>(type: "longtext", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    Condition = table.Column<string>(type: "longtext", nullable: false),
                    SerialNo = table.Column<string>(type: "longtext", nullable: true),
                    IsManualAdjust = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Remarks = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptLines_GoodsReceipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "GoodsReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptLines_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    MovementType = table.Column<string>(type: "longtext", nullable: false),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    PartNo = table.Column<string>(type: "varchar(255)", nullable: false),
                    FromLocationId = table.Column<int>(type: "int", nullable: true),
                    ToLocationId = table.Column<int>(type: "int", nullable: true),
                    PartUnitId = table.Column<int>(type: "int", nullable: true),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    Condition = table.Column<string>(type: "longtext", nullable: false),
                    RefType = table.Column<string>(type: "longtext", nullable: true),
                    RefId = table.Column<string>(type: "longtext", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    SerialNo = table.Column<string>(type: "longtext", nullable: true),
                    Remarks = table.Column<string>(type: "longtext", nullable: true),
                    UserName = table.Column<string>(type: "longtext", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_PartUnits_PartUnitId",
                        column: x => x.PartUnitId,
                        principalTable: "PartUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StockMovements_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AtmModelParts_AtmModelId_PartNo",
                table: "AtmModelParts",
                columns: new[] { "AtmModelId", "PartNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AtmModelParts_PartId",
                table: "AtmModelParts",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReportImportRows_BatchId",
                table: "DailyReportImportRows",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_DisposalRequests_PartId",
                table: "DisposalRequests",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_EquivalentGroupMembers_GroupId_PartNo",
                table: "EquivalentGroupMembers",
                columns: new[] { "GroupId", "PartNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquivalentGroupMembers_PartId",
                table: "EquivalentGroupMembers",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_EquivalentParts_OriginalPartNo_EquivalentPartNo",
                table: "EquivalentParts",
                columns: new[] { "OriginalPartNo", "EquivalentPartNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLines_GoodsReceiptId",
                table: "GoodsReceiptLines",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLines_PartId",
                table: "GoodsReceiptLines",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_LocationId",
                table: "GoodsReceipts",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_VendorId",
                table: "GoodsReceipts",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PartImages_PartId",
                table: "PartImages",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_Parts_CategoryId",
                table: "Parts",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Parts_PartNo",
                table: "Parts",
                column: "PartNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartStocks_LocationId",
                table: "PartStocks",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PartStocks_PartId_LocationId",
                table: "PartStocks",
                columns: new[] { "PartId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartUnits_LocationId",
                table: "PartUnits",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PartUnits_PartId",
                table: "PartUnits",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_PartUnits_SerialNo",
                table: "PartUnits",
                column: "SerialNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_PartId",
                table: "ReturnRequests",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_TicketId",
                table: "ReturnRequests",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_PartId",
                table: "StockCountLines",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_StockCountId",
                table: "StockCountLines",
                column: "StockCountId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PartId_Timestamp",
                table: "StockMovements",
                columns: new[] { "PartId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PartNo_Timestamp",
                table: "StockMovements",
                columns: new[] { "PartNo", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PartUnitId",
                table: "StockMovements",
                column: "PartUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_PartId",
                table: "StockTransfers",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketAttachments_TicketId",
                table: "TicketAttachments",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketAttachments_WithdrawBatchId",
                table: "TicketAttachments",
                column: "WithdrawBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketPartLines_PartId",
                table: "TicketPartLines",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketPartLines_TicketId",
                table: "TicketPartLines",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketPartLines_WithdrawBatchId",
                table: "TicketPartLines",
                column: "WithdrawBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_ExternalTicketNo",
                table: "Tickets",
                column: "ExternalTicketNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawBatches_TicketId",
                table: "WithdrawBatches",
                column: "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AtmModelParts");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "DailyReportImportRows");

            migrationBuilder.DropTable(
                name: "DisposalRequests");

            migrationBuilder.DropTable(
                name: "EquivalentGroupMembers");

            migrationBuilder.DropTable(
                name: "EquivalentParts");

            migrationBuilder.DropTable(
                name: "FeContacts");

            migrationBuilder.DropTable(
                name: "GoodsReceiptLines");

            migrationBuilder.DropTable(
                name: "PartImages");

            migrationBuilder.DropTable(
                name: "PartStocks");

            migrationBuilder.DropTable(
                name: "ReturnRequests");

            migrationBuilder.DropTable(
                name: "SavedAddresses");

            migrationBuilder.DropTable(
                name: "StockCountLines");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "StockTransfers");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "TicketAttachments");

            migrationBuilder.DropTable(
                name: "TicketPartLines");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "AtmModels");

            migrationBuilder.DropTable(
                name: "DailyReportImportBatches");

            migrationBuilder.DropTable(
                name: "EquivalentGroups");

            migrationBuilder.DropTable(
                name: "GoodsReceipts");

            migrationBuilder.DropTable(
                name: "StockCounts");

            migrationBuilder.DropTable(
                name: "PartUnits");

            migrationBuilder.DropTable(
                name: "WithdrawBatches");

            migrationBuilder.DropTable(
                name: "Vendors");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Parts");

            migrationBuilder.DropTable(
                name: "Tickets");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}

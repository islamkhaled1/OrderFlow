using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryAuthOutboxIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Existing demo products had no inventory tracking: give them a starting stock.
            migrationBuilder.Sql("UPDATE Products SET StockQuantity = 100");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Orders",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "OrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Scope = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DeadLetteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Price_NonNegative",
                table: "Products",
                sql: "[Price] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_StockQuantity_NonNegative",
                table: "Products",
                sql: "[StockQuantity] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_CreatedAt_Id",
                table: "Orders",
                columns: new[] { "CustomerId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_CreatedAt",
                table: "Orders",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Total_NonNegative",
                table: "Orders",
                sql: "[Total] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderItems_Quantity_Positive",
                table: "OrderItems",
                sql: "[Quantity] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderItems_UnitPrice_NonNegative",
                table: "OrderItems",
                sql: "[UnitPrice] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAtUtc",
                table: "IdempotencyRecords",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_IdempotencyRecords_Scope_Key",
                table: "IdempotencyRecords",
                columns: new[] { "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_DeadLettered",
                table: "OutboxMessages",
                column: "DeadLetteredAtUtc",
                filter: "[DeadLetteredAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages",
                column: "NextAttemptAtUtc",
                filter: "[ProcessedAtUtc] IS NULL AND [DeadLetteredAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Processed",
                table: "OutboxMessages",
                column: "ProcessedAtUtc",
                filter: "[ProcessedAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_CustomerId",
                table: "Users",
                column: "CustomerId",
                unique: true,
                filter: "[CustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Price_NonNegative",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_StockQuantity_NonNegative",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CustomerId_CreatedAt_Id",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status_CreatedAt",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Total_NonNegative",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItems_Quantity_Positive",
                table: "OrderItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItems_UnitPrice_NonNegative",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "OrderItems");
        }
    }
}

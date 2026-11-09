using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// PL3-M5 (pilot slice PL3.11): the price list tables, empty, and the permission <c>pricelist.manage</c> in the same migration. <c>PriceList</c>: a name, at most one default list and at most one list per
    /// customer; <c>PriceListItem</c>: a price per product and unit. The database keeps what a race could break, among live rows only (a deleted list frees its name, its customer and the default): one default
    /// (filtered unique index on <c>IsDefault = 1</c>), one list per customer (filtered unique index on <c>CustomerId</c>), one item per list, product and unit (the index of the price lookup too, it includes
    /// the price), one name. Two CHECKs say what the domain also refuses: the default list has no customer, a price is above 0 (the tables are empty, so they cost nothing to add and validate every later row).
    /// The grant: <c>pricelist.manage</c> goes to the Administrator (every code of the catalog), the Owner and the Supervisor, as the pilot slice 3 plan D4 says, and only while the role holds what the permission
    /// requires (<c>masterdata.read</c>), because an administrator may already have taken one from a system role on a populated database and a role without it would not be a closed set: such a role is left
    /// alone and can be given the right in the role editor. The rows are a FROZEN statement of what this release ships (the code strings below, not the live catalog), in one statement; running it again adds
    /// nothing. Down refuses while any price list exists (live or soft-deleted), because dropping the tables would lose them silently; with none left it removes every grant of the code, whichever role holds it
    /// (the previous release does not know it), and drops the tables, all in ONE batch, so a refusal changes nothing. Deploy note as for PL3.1: tokens refresh the new permission within 5 minutes.
    /// </summary>
    public partial class AddPriceLists : Migration
    {
        private const string PricelistManage = "pricelist.manage";
        private const string MasterDataRead = "masterdata.read";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceList",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    REC_DELETED = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    REC_CREATOR = table.Column<int>(type: "int", nullable: true),
                    REC_CREDATE = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    REC_REVISOR = table.Column<int>(type: "int", nullable: true),
                    REC_REVDATE = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceList", x => x.Id);
                    table.CheckConstraint("CK_PriceList_DefaultHasNoCustomer", "[IsDefault] = 0 OR [CustomerId] IS NULL");
                    table.ForeignKey(
                        name: "FK_PriceList_Partner_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Partner",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceListItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PriceListId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: false),
                    ListPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    REC_DELETED = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    REC_CREATOR = table.Column<int>(type: "int", nullable: true),
                    REC_CREDATE = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    REC_REVISOR = table.Column<int>(type: "int", nullable: true),
                    REC_REVDATE = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceListItem", x => x.Id);
                    table.CheckConstraint("CK_PriceListItem_ListPrice", "[ListPrice] > 0");
                    table.ForeignKey(
                        name: "FK_PriceListItem_PriceList_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "PriceList",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceListItem_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceListItem_Uom_UomId",
                        column: x => x.UomId,
                        principalTable: "Uom",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceList_CustomerId",
                table: "PriceList",
                column: "CustomerId",
                unique: true,
                filter: "([CustomerId] IS NOT NULL) AND [REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PriceList_IsDefault",
                table: "PriceList",
                column: "IsDefault",
                unique: true,
                filter: "([IsDefault] = 1) AND [REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PriceList_Name",
                table: "PriceList",
                column: "Name",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_PriceList_PublicId",
                table: "PriceList",
                column: "PublicId",
                unique: true)
                .Annotation("SqlServer:FillFactor", 90);

            migrationBuilder.CreateIndex(
                name: "IX_PriceListItem_PriceListId_ProductId_UomId",
                table: "PriceListItem",
                columns: new[] { "PriceListId", "ProductId", "UomId" },
                unique: true,
                filter: "[REC_DELETED] = 0")
                .Annotation("SqlServer:Include", new[] { "ListPrice" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceListItem_ProductId",
                table: "PriceListItem",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceListItem_UomId",
                table: "PriceListItem",
                column: "UomId");

            migrationBuilder.Sql($"""
                SET QUOTED_IDENTIFIER ON; -- RolePermission has a filtered unique index; sqlcmd has this OFF by default
                INSERT INTO [RolePermission] ([RoleId], [PermissionCode])
                SELECT r.[Id], g.[PermissionCode]
                FROM [Role] r
                JOIN (VALUES
                    (N'Admin', N'{PricelistManage}'), (N'Owner', N'{PricelistManage}'), (N'Supervisor', N'{PricelistManage}')
                ) g ([RoleCode], [PermissionCode]) ON g.[RoleCode] = r.[Code]
                WHERE r.[IsSystem] = 1 AND r.[REC_DELETED] = 0
                  AND EXISTS (SELECT 1 FROM [RolePermission] m WHERE m.[RoleId] = r.[Id] AND m.[PermissionCode] = N'{MasterDataRead}' AND m.[REC_DELETED] = 0)
                  AND NOT EXISTS (SELECT 1 FROM [RolePermission] p WHERE p.[RoleId] = r.[Id] AND p.[PermissionCode] = g.[PermissionCode] AND p.[REC_DELETED] = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The check, the grant removal and the drops are ONE batch, on purpose (see AddPurchaseOrders): THROW ends the batch, so nothing is removed. Soft-deleted lists count: the rows are still there.
            migrationBuilder.Sql($"""
                SET XACT_ABORT ON; -- a run by hand that is stopped below leaves no open transaction
                SET QUOTED_IDENTIFIER ON; -- the grant rows are in a table with a filtered unique index
                IF EXISTS (SELECT 1 FROM [PriceList])
                    THROW 50010, N'Price lists exist. Dropping the PriceList and PriceListItem tables would lose them. Restore the backup, or delete the price lists first, then run the migration again.', 1;
                DELETE FROM [RolePermission] WHERE [PermissionCode] = N'{PricelistManage}';
                DROP TABLE [PriceListItem];
                DROP TABLE [PriceList];
                """);
        }
    }
}

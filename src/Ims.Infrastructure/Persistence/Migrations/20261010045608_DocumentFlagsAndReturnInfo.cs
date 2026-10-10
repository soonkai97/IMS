using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentFlagsAndReturnInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLarge",
                table: "StockDocument",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "StockDocument",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RejectedBy",
                table: "StockDocument",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresOverride",
                table: "StockDocument",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_PostedAt",
                table: "StockDocument",
                column: "PostedAt",
                filter: "[REC_DELETED] = 0 AND [PostedAt] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "Status", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_RejectedBy",
                table: "StockDocument",
                column: "RejectedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_StockDocument_User_RejectedBy",
                table: "StockDocument",
                column: "RejectedBy",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockDocument_User_RejectedBy",
                table: "StockDocument");

            migrationBuilder.DropIndex(
                name: "IX_StockDocument_PostedAt",
                table: "StockDocument");

            migrationBuilder.DropIndex(
                name: "IX_StockDocument_RejectedBy",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "IsLarge",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "RejectedBy",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "RequiresOverride",
                table: "StockDocument");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace node.Migrations
{
    /// <inheritdoc />
    public partial class SmartContracts_v2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_PowBlocks_PowBlockId",
                table: "Transactions");

            migrationBuilder.RenameColumn(
                name: "State",
                table: "Contracts",
                newName: "Manifest");

            migrationBuilder.AddColumn<long>(
                name: "EntryPoint",
                table: "Contracts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Height = table.Column<long>(type: "INTEGER", nullable: false),
                    Snapshot = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ContractId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "fk_contract_snapshot",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractSnapshots_ContractId",
                table: "ContractSnapshots",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "ix_snapshot_height",
                table: "ContractSnapshots",
                column: "Height");

            migrationBuilder.AddForeignKey(
                name: "fk_pow_tx",
                table: "Transactions",
                column: "PowBlockId",
                principalTable: "PowBlocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pow_tx",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "ContractSnapshots");

            migrationBuilder.DropColumn(
                name: "EntryPoint",
                table: "Contracts");

            migrationBuilder.RenameColumn(
                name: "Manifest",
                table: "Contracts",
                newName: "State");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_PowBlocks_PowBlockId",
                table: "Transactions",
                column: "PowBlockId",
                principalTable: "PowBlocks",
                principalColumn: "Id");
        }
    }
}

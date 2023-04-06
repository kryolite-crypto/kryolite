using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace node.Migrations
{
    /// <inheritdoc />
    public partial class KryoliteStandardTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ConsumeToken",
                table: "Effects",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "From",
                table: "Effects",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TokenId",
                table: "Effects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenId = table.Column<string>(type: "TEXT", nullable: false),
                    IsConsumed = table.Column<bool>(type: "INTEGER", nullable: false),
                    WalletId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContractId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token", x => x.Id);
                    table.ForeignKey(
                        name: "fk_contract_tokens",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wallet_token",
                        column: x => x.WalletId,
                        principalTable: "LedgerWallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_token_tokenid",
                table: "Tokens",
                column: "TokenId");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_ContractId",
                table: "Tokens",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_WalletId",
                table: "Tokens",
                column: "WalletId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tokens");

            migrationBuilder.DropColumn(
                name: "ConsumeToken",
                table: "Effects");

            migrationBuilder.DropColumn(
                name: "From",
                table: "Effects");

            migrationBuilder.DropColumn(
                name: "TokenId",
                table: "Effects");
        }
    }
}

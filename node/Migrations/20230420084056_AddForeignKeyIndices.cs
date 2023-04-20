using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace node.Migrations
{
    /// <inheritdoc />
    public partial class AddForeignKeyIndices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pos_tx",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_pos_vote",
                table: "Votes");

            migrationBuilder.DropIndex(
                name: "IX_Votes_PosBlockId",
                table: "Votes");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_PosBlockId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PosBlockId",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "PosBlockId",
                table: "Transactions");

            migrationBuilder.RenameIndex(
                name: "IX_PowBlocks_PosBlockId",
                table: "PowBlocks",
                newName: "ix_pow_posblockid");

            migrationBuilder.RenameIndex(
                name: "IX_Effects_TransactionId",
                table: "Effects",
                newName: "ix_effect_txid");

            migrationBuilder.AddColumn<Guid>(
                name: "BlockId",
                table: "Votes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BlockId",
                table: "Transactions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_vote_blockid",
                table: "Votes",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "ix_tx_blockid",
                table: "Transactions",
                column: "BlockId");

            migrationBuilder.AddForeignKey(
                name: "fk_pos_vote",
                table: "Votes",
                column: "BlockId",
                principalTable: "PosBlocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pos_vote",
                table: "Votes");

            migrationBuilder.DropIndex(
                name: "ix_vote_blockid",
                table: "Votes");

            migrationBuilder.DropIndex(
                name: "ix_tx_blockid",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BlockId",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "BlockId",
                table: "Transactions");

            migrationBuilder.RenameIndex(
                name: "ix_pow_posblockid",
                table: "PowBlocks",
                newName: "IX_PowBlocks_PosBlockId");

            migrationBuilder.RenameIndex(
                name: "ix_effect_txid",
                table: "Effects",
                newName: "IX_Effects_TransactionId");

            migrationBuilder.AddColumn<Guid>(
                name: "PosBlockId",
                table: "Votes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PosBlockId",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Votes_PosBlockId",
                table: "Votes",
                column: "PosBlockId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PosBlockId",
                table: "Transactions",
                column: "PosBlockId");

            migrationBuilder.AddForeignKey(
                name: "fk_pos_tx",
                table: "Transactions",
                column: "PosBlockId",
                principalTable: "PosBlocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_pos_vote",
                table: "Votes",
                column: "PosBlockId",
                principalTable: "PosBlocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

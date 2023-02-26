using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace node.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChainState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chain_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: false),
                    Owner = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Balance = table.Column<long>(type: "INTEGER", nullable: false),
                    Code = table.Column<byte[]>(type: "BLOB", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LedgerWallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: false),
                    Balance = table.Column<long>(type: "INTEGER", nullable: false),
                    Pending = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_wallet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PosBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Height = table.Column<long>(type: "INTEGER", nullable: false),
                    ParentHash = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    SignedBy = table.Column<string>(type: "TEXT", nullable: false),
                    Signature = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PohjolaChain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalWork = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CurrentDifficulty = table.Column<uint>(type: "INTEGER", nullable: false),
                    CurrentReward = table.Column<int>(type: "INTEGER", nullable: false),
                    LastHash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pow", x => x.Id);
                    table.ForeignKey(
                        name: "fk_cs_pow",
                        column: x => x.Id,
                        principalTable: "ChainState",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TuonelaChain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<long>(type: "INTEGER", nullable: false),
                    SampoCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastHash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pos", x => x.Id);
                    table.ForeignKey(
                        name: "fk_cs_pos",
                        column: x => x.Id,
                        principalTable: "ChainState",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PowBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PosBlockId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Height = table.Column<long>(type: "INTEGER", nullable: false),
                    ParentHash = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    Nonce = table.Column<string>(type: "TEXT", nullable: false),
                    Difficulty = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pow", x => x.Id);
                    table.ForeignKey(
                        name: "fk_pos_pow",
                        column: x => x.PosBlockId,
                        principalTable: "PosBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Votes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Height = table.Column<long>(type: "INTEGER", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", nullable: false),
                    Signature = table.Column<string>(type: "TEXT", nullable: false),
                    PosBlockId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vote", x => x.Id);
                    table.ForeignKey(
                        name: "fk_pos_vote",
                        column: x => x.PosBlockId,
                        principalTable: "PosBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionType = table.Column<byte>(type: "INTEGER", nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", nullable: true),
                    To = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxFee = table.Column<long>(type: "INTEGER", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Nonce = table.Column<int>(type: "INTEGER", nullable: false),
                    Signature = table.Column<string>(type: "TEXT", nullable: true),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    From = table.Column<string>(type: "TEXT", nullable: true),
                    PosBlockId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PowBlockId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tx", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_PowBlocks_PowBlockId",
                        column: x => x.PowBlockId,
                        principalTable: "PowBlocks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "fk_pos_tx",
                        column: x => x.PosBlockId,
                        principalTable: "PosBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Effects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    To = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_effect", x => x.Id);
                    table.ForeignKey(
                        name: "fk_tx_effect",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_address",
                table: "Contracts",
                column: "Address");

            migrationBuilder.CreateIndex(
                name: "IX_Effects_TransactionId",
                table: "Effects",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_wallet_address",
                table: "LedgerWallets",
                column: "Address");

            migrationBuilder.CreateIndex(
                name: "ix_pos_height",
                table: "PosBlocks",
                column: "Height");

            migrationBuilder.CreateIndex(
                name: "ix_pow_height",
                table: "PowBlocks",
                column: "Height");

            migrationBuilder.CreateIndex(
                name: "IX_PowBlocks_PosBlockId",
                table: "PowBlocks",
                column: "PosBlockId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PosBlockId",
                table: "Transactions",
                column: "PosBlockId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PowBlockId",
                table: "Transactions",
                column: "PowBlockId");

            migrationBuilder.CreateIndex(
                name: "ix_tx_from",
                table: "Transactions",
                column: "From");

            migrationBuilder.CreateIndex(
                name: "ix_tx_hash",
                table: "Transactions",
                column: "Hash");

            migrationBuilder.CreateIndex(
                name: "ix_tx_to",
                table: "Transactions",
                column: "To");

            migrationBuilder.CreateIndex(
                name: "ix_vote_height",
                table: "Votes",
                column: "Height");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_PosBlockId",
                table: "Votes",
                column: "PosBlockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Contracts");

            migrationBuilder.DropTable(
                name: "Effects");

            migrationBuilder.DropTable(
                name: "LedgerWallets");

            migrationBuilder.DropTable(
                name: "PohjolaChain");

            migrationBuilder.DropTable(
                name: "TuonelaChain");

            migrationBuilder.DropTable(
                name: "Votes");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "ChainState");

            migrationBuilder.DropTable(
                name: "PowBlocks");

            migrationBuilder.DropTable(
                name: "PosBlocks");
        }
    }
}

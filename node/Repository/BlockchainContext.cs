using System.Numerics;
using Kryolite.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;

namespace Kryolite.Node;

public class BlockchainContext : DbContext
{
    public DbSet<PosBlock> PosBlocks { get; set; }
    public DbSet<PowBlock> PowBlocks { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<Vote> Votes { get; set; }
    public DbSet<LedgerWallet> LedgerWallets { get; set; }
    public DbSet<ChainState> ChainState { get; set; }

    public BlockchainContext(DbContextOptions<BlockchainContext> options)
      :base(options)
    {

    }

     protected override void OnModelCreating(ModelBuilder builder)
     {
        var diffConverter = new ValueConverter<Difficulty, uint>(
            v => v.Value,
            v => new Difficulty() { Value = v });

        var sha256Converter = new ValueConverter<SHA256Hash, byte[]>(
            v => v.Buffer,
            v => new SHA256Hash() { Buffer = v });

        var bigIntConverter = new ValueConverter<BigInteger, byte[]>(
            v => v.ToByteArray(),
            v => new BigInteger(v));

        var addrConverter = new ValueConverter<Address, byte[]>(
            v => v.Buffer,
            v => new Address { Buffer = v });

        var signConverter = new ValueConverter<Signature, byte[]>(
            v => v.Buffer,
            v => new Signature { Buffer = v });

        var pubKeyConverter = new ValueConverter<PublicKey, byte[]>(
            v => v.Buffer,
            v => new PublicKey { Buffer = v });

        var nonceConverter = new ValueConverter<Nonce, byte[]>(
            v => v.Buffer,
            v => new Nonce { Buffer = v });

        builder.Entity<PosBlock>(entity => {
            entity.ToTable("PosBlocks")
                .HasKey(e => e.Id)
                .HasName("pk_pos");

            entity.HasIndex(x => x.Height)
                .HasDatabaseName("ix_pos_height");
            
            entity.HasOne(e => e.Pow)
                .WithOne(e => e.Pos)
                .HasForeignKey<PowBlock>(x => x.PosBlockId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_pos_pow");

            entity.HasMany(e => e.Transactions)
                .WithOne()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_pos_tx");

            entity.HasMany(e => e.Votes)
                .WithOne()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_pos_vote");

            entity.Property(x => x.ParentHash)
                .HasConversion(sha256Converter);

            entity.Property(x => x.Signature)
                .HasConversion(signConverter);

            entity.Property(x => x.SignedBy)
                .HasConversion(pubKeyConverter);
        });

        builder.Entity<PowBlock>(entity => {
            entity.ToTable("PowBlocks")
                .HasKey(e => e.Id)
                .HasName("pk_pow");
            
            entity.HasIndex(x => x.Height)
                .HasDatabaseName("ix_pow_height");

            entity.Property(x => x.Difficulty)
                .HasConversion(diffConverter);

            entity.Property(x => x.ParentHash)
                .HasConversion(sha256Converter);

            entity.Property(x => x.Nonce)
                .HasConversion(nonceConverter);
        });

        builder.Entity<Transaction>(entity => {
            entity.ToTable("Transactions")
                .HasKey(e => e.Id)
                .HasName("pk_tx");
            
            entity.HasIndex(x => x.To)
                .HasDatabaseName("ix_tx_to");

            entity.Property(x => x.To)
                .HasConversion(addrConverter);

            entity.Property(x => x.Signature)
                .HasConversion(signConverter);

            entity.Property(x => x.PublicKey)
                .HasConversion(pubKeyConverter);
        });

        builder.Entity<Vote>(entity => {
            entity.ToTable("Votes")
                .HasKey(e => e.Id)
                .HasName("pk_vote");
            
            entity.HasIndex(x => x.Height)
                .HasDatabaseName("ix_vote_height");

            entity.Property(x => x.Hash)
                .HasConversion(sha256Converter);

            entity.Property(x => x.Signature)
                .HasConversion(signConverter);

            entity.Property(x => x.PublicKey)
                .HasConversion(pubKeyConverter);
        });

        builder.Entity<LedgerWallet>(entity => {
            entity.ToTable("LedgerWallets")
                .HasKey(e => e.Id)
                .HasName("pk_ledger_wallet");
            
            entity.HasIndex(x => x.Address)
                .HasDatabaseName("ix_ledger_wallet_address");

            entity.Property(x => x.Address)
                .HasConversion(addrConverter);
        });

        builder.Entity<ChainState>(entity => {
            entity.ToTable("ChainState")
                .HasKey(e => e.Id)
                .HasName("pk_chain_state");

            entity.HasOne(x => x.POS)
                .WithOne()
                .HasForeignKey<TuonelaChain>(x => x.Id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_cs_pos");

            entity.HasOne(x => x.POW)
                .WithOne()
                .HasForeignKey<PohjolaChain>(x => x.Id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_cs_pow");
        });

        builder.Entity<PohjolaChain>(entity => {
            entity.HasKey(x => x.Id)
                .HasName("pk_pow");

            entity.Property(x => x.CurrentDifficulty)
                .HasConversion(diffConverter);

            entity.Property(x => x.LastHash)
                .HasConversion(sha256Converter);

            entity.Property(x => x.TotalWork)
                .HasConversion(bigIntConverter);
        });

        builder.Entity<TuonelaChain>(entity => {
            entity.HasKey(x => x.Id)
                .HasName("pk_pos");

            entity.Property(x => x.LastHash)
                .HasConversion(sha256Converter);
        });
     }
}

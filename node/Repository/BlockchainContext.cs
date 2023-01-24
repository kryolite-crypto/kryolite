using System.Numerics;
using Kryolite.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;

namespace Kryolite.Node;

public class BlockchainContext : DbContext, IDesignTimeDbContextFactory<BlockchainContext>
{
    public DbSet<PosBlock> PosBlocks => Set<PosBlock>();
    public DbSet<PowBlock> PowBlocks => Set<PowBlock>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Vote> Votes => Set<Vote>();
    public DbSet<LedgerWallet> LedgerWallets => Set<LedgerWallet>();
    //public DbSet<LedgerAsset> LedgerAssets => Set<LedgerAsset>();
    public DbSet<ChainState> ChainState => Set<ChainState>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Effect> Effects => Set<Effect>();

    public BlockchainContext() : base()
    {

    }

    public BlockchainContext(DbContextOptions<BlockchainContext> options)
      :base(options)
    {

    }

     protected override void OnModelCreating(ModelBuilder builder)
     {
        var diffConverter = new ValueConverter<Difficulty, uint>(
            v => v.Value,
            v => new Difficulty() { Value = v });

        var bigIntConverter = new ValueConverter<BigInteger, byte[]>(
            v => v.ToByteArray(),
            v => new BigInteger(v));

        var sha256Converter = new ValueConverter<SHA256Hash, string>(
            v => v.ToString(),
            v => v);

        var addrConverter = new ValueConverter<Address, string>(
            v => v.ToString(),
            v => v);

        var signConverter = new ValueConverter<Signature, string>(
            v => v.ToString(),
            v => v);

        var pubKeyConverter = new ValueConverter<PublicKey, string>(
            v => v.ToString(),
            v => v);

        var nonceConverter = new ValueConverter<Nonce, string>(
            v => v.ToString(),
            v => v);

        var ulongConverter = new ValueConverter<ulong, long>(
            v => (long)v,
            v => (ulong)v);

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
            
            entity.HasIndex(x => x.From)
                .HasDatabaseName("ix_tx_from");

            entity.HasIndex(x => x.To)
                .HasDatabaseName("ix_tx_to");

            entity.HasIndex(x => x.Hash)
                .HasDatabaseName("ix_tx_hash");

            entity.Property(x => x.From)
                .HasConversion(addrConverter);

            entity.Property(x => x.To)
                .HasConversion(addrConverter);

            entity.Property(x => x.Signature)
                .HasConversion(signConverter);

            entity.Property(x => x.PublicKey)
                .HasConversion(pubKeyConverter);

            entity.Property(x => x.Hash)
                .HasConversion(sha256Converter);

            entity.Property(x => x.Value)
                .HasConversion(ulongConverter);

            entity.Property(x => x.MaxFee)
                .HasConversion(ulongConverter);

            entity.HasMany(e => e.Effects)
                .WithOne()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tx_effect");
        });

        builder.Entity<Effect>(entity => {
            entity.ToTable("Effects")
                .HasKey(e => e.Id)
                .HasName("pk_effect");

            entity.Property(x => x.To)
                .HasConversion(addrConverter);

            entity.Property(x => x.Value)
                .HasConversion(ulongConverter);
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

            entity.Property(x => x.Balance)
                .HasConversion(ulongConverter);

            entity.Property(x => x.Pending)
                .HasConversion(ulongConverter);

            /*entity.HasMany(x => x.Assets)
                .WithOne()
                .HasForeignKey(x => x.Address)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_lwallet_lassets");*/
        });

        /*builder.Entity<LedgerAsset>(entity => {
            entity.ToTable("LedgerAssets")
                .HasKey(e => e.Token)
                .HasName("pk_ledger_asset");
            
            entity.Property(x => x.Address)
                .HasConversion(addrConverter);

            entity.Property(x => x.Token)
                .HasConversion(sha256Converter);
        });*/

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

        builder.Entity<Contract>(entity => {
            entity.ToTable("Contracts")
                .HasKey(x => x.Id)
                .HasName("pk_contracts");

            entity.HasIndex(x => x.Address)
                .HasDatabaseName("ix_contract_address");

            entity.Property(x => x.Address)
                .HasConversion(addrConverter);

            entity.Property(x => x.Owner)
                .HasConversion(addrConverter);

            entity.Property(x => x.Balance)
                .HasConversion(ulongConverter);
        });
     }

    public BlockchainContext CreateDbContext(string[] args)
    {
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite", "blocks.dat");

        var optionsBuilder = new DbContextOptionsBuilder<BlockchainContext>();
        optionsBuilder.UseSqlite($"Data Source={path}");

        return new BlockchainContext(optionsBuilder.Options);
    }
}

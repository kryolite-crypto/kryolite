using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kryolite.Node;

public class BlockchainContext : DbContext, IDesignTimeDbContextFactory<BlockchainContext>
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Genesis> Genesis => Set<Genesis>();
    public DbSet<View> Views => Set<View>();
    public DbSet<Vote> Votes => Set<Vote>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<ChainState> ChainState => Set<ChainState>();

    public DbSet<LedgerWallet> LedgerWallets => Set<LedgerWallet>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractSnapshot> ContractSnapshots => Set<ContractSnapshot>();
    public DbSet<Effect> Effects => Set<Effect>();
    public DbSet<Token> Tokens => Set<Token>();

    public BlockchainContext() : base()
    {

    }

    public BlockchainContext(DbContextOptions<BlockchainContext> options) : base(options)
    {

    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Reduces EF memory usage by effectivily disabling all caching
        // optionsBuilder.UseInternalServiceProvider(new ServiceCollection().AddEntityFrameworkSqlite().BuildServiceProvider());
        base.OnConfiguring(optionsBuilder);
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

        var sha256NullableConverter = new ValueConverter<SHA256Hash?, string>(
            v => v!.ToString(),
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

        var ulongConverter = new ValueConverter<ulong, long>(
            v => (long)v,
            v => (ulong)v);

        var intPtrConverter = new ValueConverter<IntPtr, long>(
            v => (long)v,
            v => (IntPtr)v);

        var manifestConverter = new ValueConverter<ContractManifest, string>(
            v => JsonSerializer.Serialize(v, new JsonSerializerOptions()),
            v => JsonSerializer.Deserialize<ContractManifest>(v, new JsonSerializerOptions())!);

        builder.Entity<Transaction>(entity => {
            entity.ToTable("Transactions")
                .HasKey(e => e.Id)
                .HasName("pk_tx");

            entity.UseTphMappingStrategy();

            entity.HasIndex(x => x.TransactionId)
                .IsUnique()
                .HasDatabaseName("ix_tx_txid");

            entity.HasIndex(x => x.From)
                .HasDatabaseName("ix_tx_from");

            entity.HasIndex(x => x.To)
                .HasDatabaseName("ix_tx_to");

            entity.HasIndex(x => x.Height)
                .HasDatabaseName("ix_tx_height");

            entity.HasMany(p => p.Validates)
                .WithMany(c => c.ValidatedBy)
                .UsingEntity<TransactionJoin>(
                    l => l.HasOne<Transaction>()
                        .WithMany()
                        .HasForeignKey(e => e.ValidatesId)
                        .HasPrincipalKey(x => x.TransactionId)
                        .OnDelete(DeleteBehavior.Restrict),
                    r => r.HasOne<Transaction>()
                        .WithMany()
                        .HasForeignKey(e => e.ValidatedById)
                        .HasPrincipalKey(x => x.TransactionId)
                        .OnDelete(DeleteBehavior.Restrict)
                );

            entity.HasMany(e => e.Effects)
                .WithOne()
                .HasForeignKey(x => x.TransactionId)
                .HasPrincipalKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tx_effect");

            entity.Property(x => x.From)
                .HasConversion(addrConverter);

            entity.Property(x => x.To)
                .HasConversion(addrConverter!);

            entity.Property(x => x.Signature)
                .HasConversion(signConverter!);

            entity.Property(x => x.PublicKey)
                .HasConversion(pubKeyConverter!);

            entity.Property(x => x.TransactionId)
                .HasConversion(sha256Converter);

            entity.Property(x => x.Value)
                .HasConversion(ulongConverter);

            entity.Property(x => x.Pow)
                .HasConversion(sha256Converter);
        });

        builder.Entity<TransactionJoin>(entity => {
            entity.ToTable("TransactionTransaction")
                .HasKey(x => new { x.ValidatedById, x.ValidatesId })
                .HasName("pk_tx_tx");

            entity.Property(x => x.ValidatesId)
                .HasConversion(sha256Converter);

            entity.Property(x => x.ValidatedById)
                .HasConversion(sha256Converter);
        });

        builder.Entity<Effect>(entity => {
            entity.ToTable("Effects")
                .HasKey(e => e.Id)
                .HasName("pk_effect");

            entity.HasIndex(x => x.TransactionId)
                .HasDatabaseName("ix_effect_txid");

            entity.Property(x => x.From)
                .HasConversion(addrConverter);

            entity.Property(x => x.To)
                .HasConversion(addrConverter);

            entity.Property(x => x.Value)
                .HasConversion(ulongConverter);

            entity.Property(x => x.TokenId)
                .HasConversion(sha256NullableConverter);

            entity.Property(x => x.TransactionId)
                .HasConversion(sha256Converter);
        });

        builder.Entity<Genesis>(entity => {

        });

        builder.Entity<Block>(entity => {
            entity.Property(x => x.Difficulty)
                .HasConversion(diffConverter);

            entity.HasIndex(x => x.Height)
                .HasDatabaseName("ix_block_height");

            entity.Property(x => x.ParentHash)
                .HasConversion(sha256Converter);
        });

        builder.Entity<View>(entity => {
            entity.HasMany(x => x.Votes)
                .WithOne()
                .HasForeignKey(x => x.TransactionId)
                .HasPrincipalKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tx_signature");
        });

        builder.Entity<Vote>(entity => {
            entity.ToTable("Votes")
                .HasKey(e => e.Id)
                .HasName("pk_vote");

            entity.HasIndex(x => x.TransactionId)
                .IsUnique()
                .HasDatabaseName("ix_vote_txid");

            //entity.HasIndex(x => x.Height)
                //.HasDatabaseName("ix_vote_height");

            entity.Property(x => x.TransactionId)
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
                .IsUnique()
                .HasDatabaseName("ix_ledger_wallet_address");

            entity.Property(x => x.Address)
                .HasConversion(addrConverter);

            entity.Property(x => x.Balance)
                .HasConversion(ulongConverter);

            entity.Property(x => x.Pending)
                .HasConversion(ulongConverter);

            entity.HasMany(e => e.Tokens)
                .WithOne(x => x.Wallet)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_wallet_token");
        });

        builder.Entity<ChainState>(entity => {
            entity.ToTable("ChainState")
                .HasKey(e => e.Id)
                .HasName("pk_chain_state");

            entity.Property(x => x.CurrentDifficulty)
                .HasConversion(diffConverter);

            entity.Property(x => x.LastHash)
                .HasConversion(sha256Converter);
        });

        builder.Entity<Contract>(entity => {
            entity.ToTable("Contracts")
                .HasKey(x => x.Id)
                .HasName("pk_contracts");

            entity.HasIndex(x => x.Address)
                .HasDatabaseName("ix_contract_address");

            entity.HasMany(e => e.Snapshots)
                .WithOne()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_contract_snapshot");

            entity.HasMany(e => e.Tokens)
                .WithOne(t => t.Contract)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_contract_tokens");

            entity.Property(x => x.Address)
                .HasConversion(addrConverter);

            entity.Property(x => x.Owner)
                .HasConversion(addrConverter);

            entity.Property(x => x.Balance)
                .HasConversion(ulongConverter);

            entity.Property(x => x.EntryPoint)
                .HasConversion(intPtrConverter);

            entity.Property(x => x.Manifest)
                .HasConversion(manifestConverter);
        });

        builder.Entity<ContractSnapshot>(entity => {
            entity.ToTable("ContractSnapshots")
                .HasKey(x => x.Id)
                .HasName("pk_contract_snapshots");

            entity.HasIndex(x => x.Height)
                .HasDatabaseName("ix_snapshot_height");
        });

        builder.Entity<Token>(entity => {
            entity.ToTable("Tokens")
                .HasKey(e => e.Id)
                .HasName("pk_token");

            entity.HasIndex(x => x.TokenId)
                .HasDatabaseName("ix_token_tokenid");

            entity.Property(x => x.TokenId)
                .HasConversion(sha256Converter);
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

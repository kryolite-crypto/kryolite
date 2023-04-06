using Kryolite.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;

namespace Kryolite.Node;

public class WalletContext : DbContext, IDesignTimeDbContextFactory<WalletContext> {
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> Transactions => Set<WalletTransaction>();

    public WalletContext() : base() 
    {

    }

    public WalletContext(DbContextOptions<WalletContext> options)
      :base(options)
    { 

    }

     protected override void OnModelCreating(ModelBuilder builder)
     {
        var addrConverter = new ValueConverter<Address, string>(
            v => v.ToString(),
            v => v);

        var pubKeyConverter = new ValueConverter<PublicKey, string>(
            v => v.ToString(),
            v => v);

        var privateKeyConverter = new ValueConverter<PrivateKey, string>(
            v => v.ToString(),
            v => v);

        var ulongConverter = new ValueConverter<ulong, long>(
            v => (long)v,
            v => (ulong)v);

        var sha256Converter = new ValueConverter<SHA256Hash, string>(
            v => v.ToString(),
            v => v);

        builder.Entity<Wallet>(entity => {
            entity.ToTable("Wallets")
                .HasKey(e => e.Id)
                .HasName("pk_wallet");

            entity.HasIndex(x => x.Address)
                .HasDatabaseName("ix_wallet_address");

            entity.HasMany(e => e.WalletTransactions)
                .WithOne()
                .HasForeignKey(x => x.WalletId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_wallettx");

            entity.Property(x => x.PrivateKey)
                .HasConversion(privateKeyConverter);

            entity.Property(x => x.PublicKey)
                .HasConversion(pubKeyConverter);

            entity.Property(x => x.Balance)
                .HasConversion(ulongConverter);

            entity.Ignore(x => x.Updated);
        });

        builder.Entity<WalletTransaction>(entity =>
        {
            entity.ToTable("Transactions")
                .HasKey(e => e.Id)
                .HasName("pk_transaction");

            entity.HasIndex(x => x.Height)
                .HasDatabaseName("ix_tx_height");

            entity.Property(x => x.Recipient)
                .HasConversion(addrConverter);
        });
    }

    public WalletContext CreateDbContext(string[] args) {
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite", "wallet.dat");

        var optionsBuilder = new DbContextOptionsBuilder<WalletContext>();
        optionsBuilder.UseSqlite($"Data Source={path}");

        return new WalletContext(optionsBuilder.Options);
    }
}

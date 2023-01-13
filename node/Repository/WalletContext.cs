using Kryolite.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;

namespace Kryolite.Node;

public class WalletContext : DbContext
{
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> Transactions => Set<WalletTransaction>();
    //public DbSet<WalletAsset> Assets => Set<WalletAsset>();

    public WalletContext(DbContextOptions<WalletContext> options)
      :base(options)
    { 

    }

     protected override void OnModelCreating(ModelBuilder builder)
     {
        var addrConverter = new ValueConverter<Address, byte[]>(
            v => v.Buffer,
            v => new Address { Buffer = v });

        var pubKeyConverter = new ValueConverter<PublicKey, byte[]>(
            v => v.Buffer,
            v => new PublicKey { Buffer = v });

        var privateKeyConverter = new ValueConverter<PrivateKey, byte[]>(
            v => v.Buffer,
            v => new PrivateKey { Buffer = v });

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

            /*entity.HasMany(e => e.WalletAssets)
                .WithOne()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_wallettx");*/

            entity.Property(x => x.PrivateKey)
                .HasConversion(privateKeyConverter);

            entity.Property(x => x.PublicKey)
                .HasConversion(pubKeyConverter);

            entity.Ignore(x => x.Updated);
        });

        builder.Entity<WalletTransaction>(entity =>
        {
            entity.ToTable("Transactions")
                .HasKey(e => e.Id)
                .HasName("pk_transaction");

            entity.Property(x => x.Recipient)
                .HasConversion(addrConverter);
        });
     }
}

using Kryolite.Node.Migrations;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using System.Data;
using System.Data.SQLite;

namespace Kryolite.Node;

public class WalletRepository : IWalletRepository
{
    private static SQLiteConnection? Connection { get; set; }

    public WalletRepository()
    {
        if (Connection is null)
        {
            var walletPath = Path.Join(BlockchainService.DATA_PATH, "wallet.dat");
            var migrate = !Path.Exists(walletPath);

            Connection = new SQLiteConnection($"Data Source={walletPath};Version=3;");

            Connection.Open();

            if (migrate)
            {
                new WalletMigration(Connection).Baseline();
            }

            Connection.Flags |= SQLiteConnectionFlags.NoVerifyTextAffinity;
            Connection.Flags |= SQLiteConnectionFlags.NoVerifyTypeAffinity;

            using var cmd = Connection.CreateCommand();

            cmd.CommandText = $@"
                pragma threads = 4;
                pragma journal_mode = wal; 
                pragma synchronous = normal;
                pragma temp_store = default; 
                pragma mmap_size = -1;";

            cmd.ExecuteNonQuery();
        }
    }

    public void Add(Wallet wallet)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO Wallet (
                Address,
                Description,
                PublicKey,
                PrivateKey
            ) VALUES (@addr, @desc, @pubk, @priv);
        ";

        cmd.Parameters.Add(new SQLiteParameter("@addr", wallet.Address.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@desc", wallet.Description));
        cmd.Parameters.Add(new SQLiteParameter("@pubk", wallet.PublicKey.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@priv", wallet.PrivateKey.Buffer));

        cmd.ExecuteNonQuery(CommandBehavior.SequentialAccess);
    }

    public Wallet? Get(Address address)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Address,
                Description,
                PublicKey,
                PrivateKey
            FROM
                Wallet
            WHERE
                Address = @addr
        ";

        cmd.Parameters.Add(new SQLiteParameter("@addr", address.ToString()));

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        if (!reader.Read())
        {
            return null;
        }

        return Wallet.Read(reader);
    }

    public void UpdateDescription(Address address, string description)
    {
        var cmd = Connection!.CreateCommand();

        cmd.CommandText += $@"
                UPDATE
                    Wallet
                SET
                    Description = @desc
                WHERE
                    Address = @addr";

        cmd.Parameters.Add(new SQLiteParameter("@desc", description));
        cmd.Parameters.Add(new SQLiteParameter("@addr", address.ToString()));

        cmd.ExecuteNonQuery();
    }

    public Dictionary<Address, Wallet> GetWallets()
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Address,
                Description,
                PublicKey,
                PrivateKey
            FROM
                Wallet
        ";

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        var results = new Dictionary<Address, Wallet>();

        while (reader.Read())
        {
            var wallet = Wallet.Read(reader);
            results.TryAdd(wallet.Address, wallet);
        }

        return results;
    }
}

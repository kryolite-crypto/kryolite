using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DuckDB.NET.Data;
using MessagePack;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Kryolite.Shared;

public class Ledger
{
    public Address Address { get; set; }
    public ulong Balance { get; set; }
    public ulong Pending { get; set; }
    public List<Token> Tokens { get; set; } = new();

    public Ledger()
    {
        Address = new Address();
    }

    public Ledger(Address address)
    {
        Address = address;
    }

    public static Ledger Read(DuckDBDataReader reader)
    {
        return new Ledger
        {
            Address = reader.GetString(0),
            Balance = (ulong)reader.GetInt64(1),
            Pending = (ulong)reader.GetInt64(2)
        };
    }
}

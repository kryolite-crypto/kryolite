namespace Kryolite.Shared;

public class LedgerAsset
{
    public SHA256Hash Token { get; set; }
    public Address Address { get; set; }
    public string Item { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExternalLink { get; set; } = string.Empty;
    public Status Status { get; set; }
}

public enum Status 
{
    IN_POSSESS,
    SENT,
    CONSUMED
}
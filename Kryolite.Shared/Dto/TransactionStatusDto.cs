namespace Kryolite.Shared.Dto;

public partial class TransactionStatusDto(SHA256Hash transactionId, string status)
{
    public SHA256Hash TransactionId { get; set; } = transactionId;
    public string Status { get; set; } = status;
}

namespace Kryolite.Shared.Dto;

public partial class NodeDto(string url, bool isReachable, DateTime lastSeen)
{
    public string Url { get; set; } = url;
    public bool IsReachable { get; set; } = isReachable;
    public DateTime LastSeen { get; set; } = lastSeen;
}

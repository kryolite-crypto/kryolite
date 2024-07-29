using Kryolite.Type;

namespace Kryolite.Module.Validator;

internal class Leader
{
    public required PublicKey PublicKey { get; set; }
    public required long Height { get; set; }
    public required int SlotNumber { get; set; }
    public required int VoteCount { get; set; }
    public required long VoteHeight { get; set; }
}

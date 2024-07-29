using Kryolite.Interface;
using Kryolite.Shared.Blockchain;
using Kryolite.Type;

namespace Kryolite.Module.Validator;

internal class Generator : IGenerator
{
    private readonly IStoreManager _storeManager;
    private readonly IKeyRepository _keyRepository;

    public Generator(IStoreManager storeManager, IKeyRepository keyRepository)
    {
        _storeManager = storeManager;
        _keyRepository = keyRepository;
    }

    public void GenerateView()
    {
        var lastView = _storeManager.GetLastView();
        var lastHash = lastView?.GetHash() ?? SHA256Hash.NULL_HASH;

        var nextView = new View
        {
            Id = (lastView?.Id ?? 0) + 1L,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LastHash = lastHash,
            PublicKey = _keyRepository.GetPublicKey(),
            Blocks = _storeManager.GetPendingBlocks()
                .Where(x => x.LastHash == lastHash)
                .Select(x => x.GetHash())
                .ToList(),
            Votes = _storeManager.GetPendingVotes()
                .Where(x => x.ViewHash == lastHash)
                .Select(x => x.GetHash())
                .ToList(),
            Transactions = _storeManager.GetPendingTransactions()
                .Select(x => x.CalculateHash())
                .ToList()
        };

        nextView.Sign(_keyRepository.GetPrivateKey());

        _storeManager.AddView(nextView, true, true);
    }
}
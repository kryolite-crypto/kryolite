using System.Collections.Concurrent;
using Kryolite.Interface;
using Kryolite.Type;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Module.Validator;

internal class Slots : ISlots
{
    private readonly ConcurrentBag<PublicKey> _banned = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Slots> _logger;

    public Slots(IServiceProvider serviceProvider, ILogger<Slots> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public bool TryGetNextLeader(out Leader leader)
    {
        using var scope = _serviceProvider.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        var lastView = storeManager.GetLastView() ?? throw new Exception("LastView returned null");

        // offset height by one since votes are confirmed at (height % Constant.VOTE_INTERVAL + 1)
        var slotNumber = (int)((lastView.Id - 1) % Constant.VOTE_INTERVAL);
        var voteHeight = lastView.Id - slotNumber;

        var votes = storeManager.GetVotesAtHeight(voteHeight);

        _logger.LogDebug("Loading votes from height {voteHeight} (id = {id}, slotNumber = {slotNumber}, voteCount = {voteCount})",
            voteHeight,
            lastView.Id,
            slotNumber,
            votes.Count
        );

        if (slotNumber == 0)
        {
            _logger.LogInformation("View #{id} received {count} votes", voteHeight, votes.Count);
        }

        leader = new Leader
        {
            PublicKey = PublicKey.NULL_PUBLIC_KEY,
            Height = lastView.Id,
            SlotNumber = slotNumber,
            VoteHeight = voteHeight,
            VoteCount = votes.Count
        };

        if (votes.Count == 0)
        {
            return false;
        }

        var voters = votes
            .Where(x => !_banned.Contains(x.PublicKey))
            .OrderBy(x => x.Signature)
            .Select(x => x.PublicKey)
            .ToList();

        if (voters.Count == 0)
        {
            return false;
        }

        leader.PublicKey = voters[slotNumber % voters.Count];

        return true;
    }

    public void Ban(PublicKey publicKey)
    {
        _banned.Add(publicKey);
    }

    public void Clear()
    {
        _banned.Clear();
    }
}

using Kryolite.Node.Blockchain;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class Verifier : IVerifier
{
    private IStoreRepository Store { get; }
    private IStateCache StateCache { get; }
    private ILogger<Verifier> Logger { get; }

    public Verifier(IStoreRepository store, IStateCache stateCache, ILogger<Verifier> logger)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        StateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Verify(Transaction tx)
    {
        if (StateCache.GetTransactions().ContainsKey(tx.CalculateHash()) || Store.TransactionExists(tx.CalculateHash()))
        {
            Logger.LogInformation($"{tx.CalculateHash()} already exists");
            return false;
        }

        if (!tx.Verify())
        {
            Logger.LogInformation($"{tx.CalculateHash()} verification failed (reson = invalid signature)");
            return false;
        }

        if (tx.PublicKey is null || tx.PublicKey == PublicKey.NULL_PUBLIC_KEY)
        {
            Logger.LogInformation("Validator registeration verification failed (reason = null public key)");
            return false;
        }

        if (tx.To is null || tx.To == Address.NULL_ADDRESS)
        {
            Logger.LogInformation("Validator registeration verification failed (reason = 'to' address not set)");
            return false;
        }

        var success = VerifyByTransactionType(tx);
        tx.ExecutionResult = success ? ExecutionResult.PENDING : ExecutionResult.VERIFY_FAILED;
        return success;
    }

    public bool Verify(Block block)
    {
        if (StateCache.GetBlocks().ContainsKey(block.GetHash()) || Store.BlockExists(block.GetHash()))
        {
            Logger.LogInformation($"{block.GetHash()} already exists");
            return false;
        }

        if (block.To is null || block.To == Address.NULL_ADDRESS)
        {
            Logger.LogInformation("Block verification failed (reason = null to address)");
            return false;
        }

        if (block.Value != Constant.BLOCK_REWARD)
        {
            Logger.LogInformation($"Block verification failed (reason = invalid reward). Got {block.Value}, required: {Constant.BLOCK_REWARD}");
            return false;
        }

        var chainState = StateCache.GetCurrentState();

        if (chainState.ViewHash != block.LastHash)
        {
            Logger.LogInformation($"Block verification failed (reason = invalid parent hash). Got {block.LastHash}, required: {chainState.ViewHash}");
            return false;
        }

        if (block.Difficulty != StateCache.GetCurrentState().CurrentDifficulty)
        {
            Logger.LogInformation($"Block verification failed (reason = invalid difficulty). Got {block.Difficulty}, required: {StateCache.GetCurrentState().CurrentDifficulty}");
            return false;
        }

        if (!block.VerifyNonce())
        {
            Logger.LogInformation($"Block verification failed (reason = invalid nonce)");
            return false;
        }

        return true;
    }

    public bool Verify(View view)
    {
        var chainState = StateCache.GetCurrentState();

        if (view.Id != chainState.Id + 1)
        {
            Logger.LogInformation($"View verification failed (reason = invalid height). Got {view.Id}, required: {chainState.Id + 1}");
            return false;
        }

        if (view.LastHash != chainState.ViewHash)
        {
            Logger.LogInformation("Discarding view #{id} (reason = invalid parent hash)", view.Id);
            return false;
        }

        var earliest = StateCache.GetCurrentView().Timestamp + Constant.HEARTBEAT_INTERVAL;

        if (view.Timestamp < earliest)
        {
            Logger.LogInformation("Discarding view #{id} (reason = timestamp too early)", view.Id);
            return false;
        }

        if (view.Timestamp > DateTimeOffset.UtcNow.AddSeconds(15).ToUnixTimeMilliseconds())
        {
            Logger.LogInformation("Discarding view #{id} (reason = timestamp in future", view.Id);
            return false;
        }

        if (!view.Verify())
        {
            Logger.LogInformation($"{view.GetHash()} verification failed (reson = invalid signature)");
            return false;
        }

        foreach (var blockhash in view.Blocks)
        {
            if (!StateCache.GetBlocks().TryGetValue(blockhash, out var block))
            {
                Logger.LogInformation($"{view.GetHash()} verification failed (reson = block not found)");
                return false;
            }

            if (block.LastHash != chainState.ViewHash)
            {
                Logger.LogInformation($"{view.GetHash()} verification failed (reson = block references finalized view)");
                return false;
            }
        }

        // Votes are given at (Id % VOTE_INTERVAL), votes will be included in next block
        if ((view.Id % Constant.VOTE_INTERVAL) != 1 && view.Votes.Count > 0)
        {
            Logger.LogInformation($"{view.GetHash()} verification failed (reson = non-milestone view has votes)");
            return false;
        }

        foreach (var votehash in view.Votes)
        {
            if (!StateCache.GetVotes().TryGetValue(votehash, out var vote))
            {
                Logger.LogInformation($"{view.GetHash()} verification failed (reson = vote not found)");
                return false;
            }

            if (vote.ViewHash != chainState.ViewHash)
            {
                Logger.LogInformation($"{view.GetHash()} verification failed (reson = vote references finalized view)");
                return false;
            }
        }

        return true;
    }

    public bool Verify(Vote vote)
    {
        if (StateCache.GetVotes().ContainsKey(vote.GetHash()) || Store.VoteExists(vote.GetHash()))
        {
            Logger.LogInformation($"Vote {vote.GetHash()} already exists");
            return false;
        }

        if (vote.PublicKey is null || vote.PublicKey == PublicKey.NULL_PUBLIC_KEY)
        {
            Logger.LogInformation("Vote verification failed (reason = null public key)");
            return false;
        }

        var address = vote.PublicKey.ToAddress();

        if (!Store.IsValidator(address))
        {
            Logger.LogInformation($"Vote verification failed (reason = not validator ({address}))");
            return false;
        }

        if (!vote.Verify())
        {
            Logger.LogInformation($"Vote verification failed (reason = invalid signature)");
            return false;
        }

        return true;
    }

    private bool VerifyByTransactionType(Transaction tx)
    {
        switch (tx.TransactionType)
        {
            case TransactionType.PAYMENT:
                return VerifyPayment(tx);
            case TransactionType.CONTRACT:
                return true;
            case TransactionType.REG_VALIDATOR:
                return VerifyValidatorRegisteration(tx);
            default:
                throw new ArgumentException($"Invalid transaction type {tx.TransactionType} for {tx.CalculateHash()}");
        }
    }

    private bool VerifyPayment(Transaction tx)
    {
        if (!tx.To.IsContract())
        {
            if (tx.Value == 0)
            {
                Logger.LogInformation("Payment verification failed (reason = zero payment)");
                return false;
            }

            if (tx.Data?.Length > 8)
            {
                Logger.LogInformation("Payment verification failed (reason = extra data payload)");
                return false;
            }
        }

        return true;
    }

    private bool VerifyValidatorRegisteration(Transaction tx)
    {
        var isValidator = Store.IsValidator(tx.From!);

        if (!isValidator && tx.Value < Constant.MIN_STAKE)
        {
            Logger.LogInformation($"Validator registeration verification failed (reason = stake too low {tx.Value})");
            return false;
        }

        if (isValidator && tx.Value > 0 && tx.Value < Constant.MIN_STAKE)
        {
            Logger.LogInformation($"Validator registeration verification failed (reason = stake too low {tx.Value})");
            return false;
        }

        // do not allow seed validator registeration
        if (Constant.SEED_VALIDATORS.Contains(tx.From!))
        {
            Logger.LogInformation($"Validator registeration verification failed (reason = sender is seed validator)");
            return false;
        }

        return true;
    }
}
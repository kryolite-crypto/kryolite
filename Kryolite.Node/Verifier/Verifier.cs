using Kryolite.Interface;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Type;
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
        var hash = tx.CalculateHash();

        if (StateCache.GetTransactions().ContainsKey(hash) || Store.TransactionExists(hash))
        {
            Logger.LogDebug($"{hash} already exists");
            return false;
        }

        if (tx.PublicKey is null || tx.PublicKey == PublicKey.NULL_PUBLIC_KEY)
        {
            Logger.LogInformation($"{hash} verification failed (reason = null public key)");
            return false;
        }

        if (!tx.Verify())
        {
            Logger.LogInformation($"{hash} verification failed (reason = invalid signature)");
            return false;
        }

        var success = VerifyByTransactionType(tx);
        tx.ExecutionResult = success ? ExecutionResult.PENDING : ExecutionResult.VERIFY_FAILED;
        return success;
    }

    public bool Verify(Block block)
    {
        var hash = block.GetHash();

        if (StateCache.GetBlocks().ContainsKey(hash) || Store.BlockExists(hash))
        {
            Logger.LogInformation($"{hash} already exists");
            return false;
        }

        if (block.To is null || block.To == Address.NULL_ADDRESS)
        {
            Logger.LogInformation("Block verification failed (reason = null to address)");
            return false;
        }

        var chainState = StateCache.GetCurrentState();

        if (block.Value != chainState.BlockReward)
        {
            Logger.LogInformation($"Block verification failed (reason = invalid reward). Got {block.Value}, required: {chainState.BlockReward}");
            return false;
        }

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

        var earliest = StateCache.GetCurrentView().Timestamp + Constant.VIEW_INTERVAL;

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

        var address = view.PublicKey.ToAddress();
        var validator = Store.GetValidator(address);

        if (validator is null)
        {
            Logger.LogInformation($"View verification failed (reason = view generator not validator ({address}))");
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
                Logger.LogInformation($"{view.GetHash()} verification failed (reson = block not found, blockhash {blockhash})");
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

        foreach (var tx in view.Transactions)
        {
            if (!StateCache.GetTransactions().ContainsKey(tx))
            {
                Logger.LogInformation($"{view.GetHash()} verification failed (reson = tx not found ({tx}))");
                return false;
            }

            if (Store.TransactionExists(tx))
            {
                Logger.LogInformation($"{view.GetHash()} verification failed (reson = tx already persisted ({tx}))");
                return false;
            }
        }

        return true;
    }

    public bool Verify(Vote vote)
    {
        var hash = vote.GetHash();

        if (StateCache.GetVotes().ContainsKey(hash) || Store.VoteExists(hash))
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
        var validator = Store.GetValidator(address);

        if (validator is null)
        {
            Logger.LogInformation($"Vote verification failed (reason = not validator ({address}))");
            return false;
        }

        if (vote.RewardAddress != validator.RewardAddress)
        {
            Logger.LogInformation($"Vote verification failed (reason = tx recipient not reward address ({address}))");
            return false;
        }

        var stake = Constant.SEED_VALIDATORS.Contains(address) ?
            Constant.MIN_STAKE : validator.Stake;

        if (stake < Constant.MIN_STAKE)
        {
            Logger.LogInformation($"Vote verification failed (reason = stake too low, vote = {vote.Stake}, validator = {validator.Stake}, address = {validator.NodeAddress}))");
            return false;
        }

        if (vote.Stake != stake)
        {
            Logger.LogInformation($"Vote verification failed (reason = vote stake not equal to validator stake, vote = {vote.Stake}, validator = {validator.Stake}, address = {validator.NodeAddress})");
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
        return tx.TransactionType switch
        {
            TransactionType.PAYMENT => VerifyPayment(tx),
            TransactionType.CONTRACT => VerifyContract(tx),
            TransactionType.REGISTER_VALIDATOR => VerifyValidatorRegisteration(tx),
            TransactionType.DEREGISTER_VALIDATOR => VerifyValidatorDeRegisteration(tx),
            _ => ReportInvalidTransactionType(tx)
        };
    }

    private bool ReportInvalidTransactionType(Transaction tx)
    {
        Logger.LogInformation($"Invalid TransactionType = {tx.TransactionType}");
        return false;
    }

    private bool VerifyPayment(Transaction tx)
    {
        if (tx.To is null || tx.To == Address.NULL_ADDRESS)
        {
            Logger.LogInformation("Validator registeration verification failed (reason = 'to' address not set)");
            return false;
        }

        if (tx.To.IsContract())
        {
            // Fee has to be bigger than calculated base fee due to added gas fees
            if (tx.MaxFee < tx.CalculateFee())
            {
                Logger.LogInformation($"{tx.CalculateHash()} verification failed (reason = invalid fee)");
                return false;
            }
        }
        else
        {
            if (tx.MaxFee != tx.CalculateFee())
            {
                Logger.LogInformation($"{tx.CalculateHash()} verification failed (reason = invalid fee)");
                return false;
            }

            if (tx.Value == 0)
            {
                Logger.LogInformation("Payment verification failed (reason = zero payment)");
                return false;
            }
        }

        return true;
    }

    private bool VerifyContract(Transaction tx)
    {
        if (tx.To != Address.NULL_ADDRESS)
        {
            Logger.LogInformation("Validator registeration verification failed (reason = 'to' address set)");
            return false;
        }

        if (tx.MaxFee < tx.CalculateFee())
        {
            Logger.LogInformation($"{tx.CalculateHash()} verification failed (reason = invalid fee)");
            return false;
        }

        return true;
    }

    private bool VerifyValidatorRegisteration(Transaction tx)
    {
        if (tx.Value > 0)
        {
            Logger.LogInformation($"Validator registeration verification failed (reason = tx has value {tx.Value})");
            return false;
        }

        if (tx.MaxFee != 0)
        {
            Logger.LogInformation($"{tx.CalculateHash()} verification failed (reason = extra fee)");
            return false;
        }

        // do not allow seed validator registeration
        if (Constant.SEED_VALIDATORS.Contains(tx.From!))
        {
            Logger.LogInformation($"Validator registeration verification failed (reason = sender is seed validator)");
            return false;
        }

        // If we are setting stake we need to have recipient address
        if (tx.To is null || tx.To == Address.NULL_ADDRESS)
        {
            Logger.LogInformation($"Validator registeration verification failed (reason = reward recipient not set)");
            return false;
        }

        var isValidator = Store.IsValidator(tx.From!);

        if (isValidator)
        {
            Logger.LogInformation($"Validator registeration verification failed (reason = already validator {tx.From})");
            return false;
        }

        return true;
    }

    private bool VerifyValidatorDeRegisteration(Transaction tx)
    {
        if (tx.Value > 0)
        {
            Logger.LogInformation($"Validator deregisteration verification failed (reason = tx has value {tx.Value})");
            return false;
        }

        if (tx.MaxFee != 0)
        {
            Logger.LogInformation($"{tx.CalculateHash()} verification failed (reason = extra fee)");
            return false;
        }

        // do not allow seed validator deregisteration
        if (Constant.SEED_VALIDATORS.Contains(tx.From!))
        {
            Logger.LogInformation($"Validator deregisteration verification failed (reason = sender is seed validator)");
            return false;
        }

        // If we are setting stake we need to have recipient address
        if (tx.To != Address.NULL_ADDRESS)
        {
            Logger.LogInformation($"Validator deregisteration verification failed (reason = tx.To should ne NULL_ADDRESS)");
            return false;
        }

        var isValidator = Store.IsValidator(tx.From!);

        if (!isValidator)
        {
            Logger.LogInformation($"Validator deregisteration verification failed (reason = not validator {tx.From})");
            return false;
        }

        return true;
    }
}

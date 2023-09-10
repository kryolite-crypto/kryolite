using System.Collections.Concurrent;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;
using QuikGraph;
using QuikGraph.Algorithms;

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

    public void Verify(ICollection<Transaction> transactions)
    {
        Parallel.ForEach(transactions, tx =>
        {
            if (tx.TransactionId == SHA256Hash.NULL_HASH)
            {
                tx.TransactionId = tx.CalculateHash();
            }

            if (StateCache.Contains(tx.TransactionId) || Store.Exists(tx.TransactionId))
            {
                Logger.LogDebug($"{tx.TransactionId} already exists");
                tx.ExecutionResult = ExecutionResult.SUCCESS;
                return;
            }

            if (!tx.Verify())
            {
                Logger.LogInformation($"{tx.TransactionId} verification failed");
                tx.ExecutionResult = ExecutionResult.VERIFY_FAILED;
                return;
            }

            tx.ExecutionResult = ExecutionResult.VERIFYING;
        });
    }

    public bool VerifyTypeOnly(Transaction tx, ConcurrentDictionary<SHA256Hash, Transaction> transactionList)
    {
        if (tx.ExecutionResult != ExecutionResult.VERIFYING)
        {
            return false;
        }

        foreach (var parent in tx.Parents)
        {
            if (!transactionList.ContainsKey(parent) && !StateCache.Contains(parent) && !Store.Exists(parent))
            {
                Logger.LogInformation($"Unknown parent reference ({tx.TransactionId} refers to parent {parent})");
                tx.ExecutionResult = ExecutionResult.VERIFY_FAILED;
                continue;
            }
        }

        var success = VerifyByTransactionType(tx);
        tx.ExecutionResult = success ? ExecutionResult.VERIFIED : ExecutionResult.VERIFY_FAILED;

        return success;
    }

    public bool Verify(Transaction tx)
    {
        if (tx.TransactionId == SHA256Hash.NULL_HASH)
        {
            tx.TransactionId = tx.CalculateHash();
        }

        if (StateCache.Contains(tx.TransactionId) || Store.Exists(tx.TransactionId))
        {
            Logger.LogDebug($"{tx.TransactionId} already exists");
            return true;
        }

        if (!tx.Verify())
        {
            Logger.LogInformation($"{tx.TransactionId} verification failed");
            return false;
        }

        foreach (var parent in tx.Parents)
        {
            if (!StateCache.Contains(parent) && !Store.Exists(parent))
            {
                Logger.LogInformation($"Unknown parent reference ({tx.TransactionId} refers to parent {parent})");
                return false;
            }
        }

        var success = VerifyByTransactionType(tx);
        tx.ExecutionResult = success ? ExecutionResult.VERIFIED : ExecutionResult.VERIFY_FAILED;
        return success;
    }

    private bool VerifyByTransactionType(Transaction tx)
    {
        switch (tx.TransactionType)
        {
            case TransactionType.PAYMENT:
                return VerifyPayment(tx);
            case TransactionType.BLOCK:
                return VerifyBlock((Block)tx);
            case TransactionType.VIEW:
                return VerifyView((View)tx);
            case TransactionType.CONTRACT:
                return true;
            case TransactionType.REG_VALIDATOR:
                return VerifyValidatorRegisteration(tx);
            case TransactionType.VOTE:
                return VerifyVote((Vote)tx);
            default:
                throw new ArgumentException($"Invalid transaction type {tx.TransactionType} for {tx.TransactionId}");
        }
    }

    private bool VerifyPayment(Transaction tx)
    {
        if (tx.PublicKey is null)
        {
            Logger.LogInformation("Payment verification failed (reason = null public key)");
            return false;
        }

        if (tx.To is null)
        {
            Logger.LogInformation("Payment verification failed (reason = null 'to' address)");
            return false;
        }

        if (tx.Parents.Count != 2)
        {
            Logger.LogInformation($"Payment verification failed (reason = invalid parent reference count {tx.Parents.Count})");
            return false;
        }

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

    private bool VerifyBlock(Block block)
    {
        if (block.To is null)
        {
            Logger.LogInformation("Block verification failed (reason = null to address)");
            return false;
        }

        if (block.Parents.Count != 2)
        {
            Logger.LogInformation($"Block verification failed (reason = invalid parent reference count {block.Parents.Count})");
            return false;
        }

        if (block.Value != Constant.BLOCK_REWARD)
        {
            Logger.LogInformation($"Block verification failed (reason = invalid reward). Got {block.Value}, required: {Constant.BLOCK_REWARD}");
            return false;
        }

        return true;
    }

    private bool VerifyView(View view)
    {
        if (view.Parents.Count < 2)
        {
            Logger.LogInformation($"View verification failed (reason = invalid parent reference count {view.Parents.Count})");
            return false;
        }

        var chainState = StateCache.GetCurrentState();
        var height = view.Height ?? 0;

        if (height != chainState.Height + 1)
        {
            Logger.LogInformation($"View verification failed (reason = invalid height). Got {view.Height}, required: {chainState.Height + 1}");
            return false;
        }

        if (view.Value != Constant.VALIDATOR_REWARD)
        {
            Logger.LogInformation($"View verification failed (reason = invalid value). Got {view.Value}, required: {Constant.VALIDATOR_REWARD}");
            return false;
        }

        if (view.To is not null)
        {
            Logger.LogInformation("View verification failed (reason = has recipient address)");
            return false;
        }

        if (height > 0)
        {
            var earliest = StateCache.GetCurrentView().Timestamp + Constant.HEARTBEAT_INTERVAL;

            if (view.Timestamp < earliest)
            {
                Logger.LogInformation("Discarding view #{height} (reason = timestamp too early)", view.Height);
                return false;
            }
        }

        return true;
    }

    private bool VerifyVote(Vote vote)
    {
        if (vote.Parents.Count != 2)
        {
            Logger.LogInformation($"Vote verification failed (reason = invalid parent reference count {vote.Parents.Count})");
            return false;
        }

        if (vote.PublicKey is null)
        {
            Logger.LogInformation("Vote verification failed (reason = null public key)");
            return false;
        }

        if (!Store.IsValidator(vote.From!))
        {
            Logger.LogInformation($"Vote verification failed (reason = not validator ({vote.From}))");
            return false;
        }

        var stake = Store.GetStake(vote.From!);

        if (stake?.Amount != vote.Value)
        {
            Logger.LogInformation($"Vote verification failed (reason = invalid stake set ({vote.Value} / {stake}))");
            return false;
        }

        return true;
    }

    private bool VerifyValidatorRegisteration(Transaction tx)
    {
        if (tx.PublicKey is null)
        {
            Logger.LogInformation("Validator registeration verification failed (reason = null public key)");
            return false;
        }

        if (tx.To is null)
        {
            Logger.LogInformation("Validator registeration verification failed (reason = 'to' address not set)");
            return false;
        }

        if (tx.Parents.Count != 2)
        {
            Logger.LogInformation($"Validator registeration verification failed (reason = invalid parent reference count {tx.Parents.Count})");
            return false;
        }

        var isValidator = Store.IsValidator(tx.From!);

        if (!isValidator && tx.Value < Constant.MIN_STAKE)
        {
            Logger.LogInformation($"Validator registeration verification failed (reason = stake too low {tx.Value})");
            return false;
        }

        if (isValidator && (tx.Value > 0 && tx.Value < Constant.MIN_STAKE))
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
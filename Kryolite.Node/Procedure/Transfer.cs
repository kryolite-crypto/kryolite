using System.Diagnostics.CodeAnalysis;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Node.Procedure;

public readonly ref struct Transfer(IStoreRepository Repository, WalletCache Ledger, ValidatorCache Validators, ChainState ChainState)
{
    public void To(Address address, ulong value, out Ledger wallet)
    {
        if (!Ledger.TryGetWallet(address, Repository, out wallet!))
        {
            wallet = new Ledger(address);
            Ledger.Add(address, wallet);
        }

        if (wallet.Locked && Validators.TryGetValidator(address, Repository, out var validator))
        {
            validator.Stake = checked(validator.Stake + value);
            validator.Changed = true;
            ChainState.TotalActiveStake = checked(ChainState.TotalActiveStake + value);
            return;
        }

        wallet.Balance = checked(wallet.Balance + value);
        wallet.Changed = true;
    }

    public bool From(Address address, ulong value, out ExecutionResult executionResult, [NotNullWhen(true)] out Ledger? wallet)
    {
        if (!Ledger.TryGetWallet(address, Repository, out wallet))
        {
            executionResult = ExecutionResult.UNKNOWN;
            return false;
        }

        if (wallet.Locked && Validators.TryGetValidator(address, Repository, out var validator))
        {
            if (validator.Stake < value)
            {
                executionResult = ExecutionResult.TOO_LOW_BALANCE;
                return false;
            }

            validator.Stake = checked(validator.Stake - value);
            validator.Changed = true;
            ChainState.TotalActiveStake = checked(ChainState.TotalActiveStake - value);
        }
        else
        {
            if (wallet.Balance < value)
            {
                executionResult = ExecutionResult.TOO_LOW_BALANCE;
                return false;
            }

            wallet.Balance = checked(wallet.Balance - value);
            wallet.Changed = true;
        }

        executionResult = ExecutionResult.PENDING;
        return true;
    }

    public void Pending(Address address, ulong value, [NotNull] out Ledger wallet)
    {
        if (!Ledger.TryGetWallet(address, Repository, out wallet!))
        {
            wallet = new Ledger(address);
            Ledger.Add(address, wallet);
        }

        wallet.Pending = checked(wallet.Pending + value);
    }

    public bool Unlock(Address address, out ExecutionResult executionResult)
    {
        if (!Ledger.TryGetWallet(address, Repository, out var wallet))
        {
            executionResult = ExecutionResult.FAILED_TO_UNLOCK;
            return false;
        }

        if (wallet.Locked && Validators.TryGetValidator(address, Repository, out var validator))
        {
            wallet.Balance = validator.Stake;
            wallet.Locked = false;
            wallet.Changed = true;

            validator.Stake = 0;
            validator.Changed = true;

            executionResult = ExecutionResult.SUCCESS;
            return true;
        }

        executionResult = ExecutionResult.UNKNOWN;
        return false;
    }

    public bool Lock(Address address, Address rewardAddress, out ExecutionResult executionResult)
    {
        if (!Ledger.TryGetWallet(address, Repository, out var wallet))
        {
            executionResult = ExecutionResult.FAILED_TO_LOCK;
            return false;
        }

        if (!wallet.Locked)
        {
            if (!Validators.TryGetValidator(address, Repository, out var validator))
            {
                validator = new Validator
                {
                    NodeAddress = address
                };

                Validators.Add(address, validator);
            }

            validator.Stake = wallet.Balance;
            validator.RewardAddress = rewardAddress;
            validator.Changed = true;

            wallet.Balance = 0;
            wallet.Locked = true;
            wallet.Changed = true;

            executionResult = ExecutionResult.SUCCESS;
            return true;
        }

        executionResult = ExecutionResult.UNKNOWN;
        return false;
    }
}

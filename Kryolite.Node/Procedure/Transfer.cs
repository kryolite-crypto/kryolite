using System.Diagnostics.CodeAnalysis;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Node.Procedure;

public readonly ref struct Transfer(IStoreRepository Repository, WalletCache Ledger, ValidatorCache Validators)
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
            return;
        }

        wallet.Balance = checked(wallet.Balance + value);
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
        }
        else
        {
            if (wallet.Balance < value)
            {
                executionResult = ExecutionResult.TOO_LOW_BALANCE;
                return false;
            }

            wallet.Balance = checked(wallet.Balance - value);
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

        if (address == "kryo:ac973k3jdykh8gc3fir3xz3ptzzckbn6v4dnza68")
        {
            Console.WriteLine($"Unlock");
        }

        if (wallet.Locked && Validators.TryGetValidator(address, Repository, out var validator))
        {
            wallet.Balance = validator.Stake;
            wallet.Locked = false;
            validator.Stake = 0;

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

        if (address == "kryo:ac973k3jdykh8gc3fir3xz3ptzzckbn6v4dnza68")
        {
            Console.WriteLine($"Lock");
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
            wallet.Balance = 0;
            wallet.Locked = true;

            executionResult = ExecutionResult.SUCCESS;
            return true;
        }

        executionResult = ExecutionResult.UNKNOWN;
        return false;
    }
}

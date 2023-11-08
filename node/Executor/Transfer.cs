using Kryolite;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

public class Transfer(IStoreRepository Repository, WalletCache Ledger, ValidatorCache Validators)
{
    public (ExecutionResult, Ledger?, Validator?) AddTo(Address address, ulong value)
    {
        if (!Ledger.TryGetWallet(address, Repository, out var wallet))
        {
            wallet = new Ledger(address);
            Ledger.Add(address, wallet);
        }

        if (wallet.Locked && Validators.TryGetValidator(address, Repository, out var validator) && validator.Stake != Validator.INACTIVE)
        {
            validator.Stake = checked(validator.Stake + value);
            return ExecutionResult.SUCCESS;
        }

        wallet.Balance = checked(wallet.Balance + value);
        return ExecutionResult.SUCCESS;
    }

    public (ExecutionResult, Ledger?) TakeFrom(Address address, ulong value)
    {
        if (!Ledger.TryGetWallet(address, Repository, out var wallet))
        {
            return (ExecutionResult.UNKNOWN, null);
        }

        if (wallet.Locked && Validators.TryGetValidator(address, Repository, out var validator) && validator.Stake != Validator.INACTIVE)
        {
            if (validator.Stake < value)
            {
                return (ExecutionResult.TOO_LOW_BALANCE, null);
            }

            validator.Stake = checked(validator.Stake - value);
            return (ExecutionResult.SUCCESS, null);
        }

        if (wallet.Balance < value)
        {
            return (ExecutionResult.TOO_LOW_BALANCE, null);
        }

        wallet.Balance = checked(wallet.Balance - value);
        return (ExecutionResult.SUCCESS, wallet);
    }
}
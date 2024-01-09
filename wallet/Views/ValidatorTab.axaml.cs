using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kryolite.EventBus;
using Kryolite.Node;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Wallet;

public partial class ValidatorTab : UserControl
{
    private ValidatorViewModel Model = new();

    public ValidatorTab()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = Model;

        AttachedToVisualTree += async (object? sender, VisualTreeAttachmentEventArgs e) => {
            await LoadData();
        };

        var eventBus = Program.ServiceCollection.GetRequiredService<IEventBus>();

        eventBus.Subscribe<ChainState>(async chainState => {
            await LoadData();
        });
    }

    public void CopyAddress(object sender, PointerPressedEventArgs args)
    {
        TopLevel.GetTopLevel(this)?.Clipboard!.SetTextAsync(Model.Address.ToString());
    }

    public async Task LoadData()
    {
        try
        {
            using var scope = Program.ServiceCollection.CreateScope();
            var keyRepository = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

            var key = keyRepository.GetKey();
            var validator = storeManager.GetStake(key.Address);
            var chainState = storeManager.GetChainState();

            var votes = storeManager.GetVotesForAddress(key.Address, 6).Select(x => new TransactionModel
            {
                Recipient = x.To,
                Amount = (long)x.Value,
                Timestamp = x.Timestamp
            }).ToList();

            var previousEpoch = chainState.Id - (chainState.Id % Constant.EPOCH_LENGTH);
            var nextEpoch = previousEpoch + Constant.EPOCH_LENGTH;
            var estimatedReward = storeManager.GetEstimatedStakeReward(key.Address, nextEpoch);
            
            var secondsUntilEpochEnd = (nextEpoch - chainState.Id) * Constant.HEARTBEAT_INTERVAL;
            var endOfEpoch = DateTimeOffset.UtcNow.AddSeconds(secondsUntilEpochEnd);

            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.Address = key.Address;

                if (validator is not null)
                {
                    if (validator.Stake >= Constant.MIN_STAKE)
                    {
                        Model.Status = "Enabled";
                        Model.ActionText = "Disable Validator";
                    }
                    else
                    {
                        Model.Status = "Disabled";
                        Model.ActionText = "Enable Validator";
                    }

                    Model.RewardAddress = validator.RewardAddress != Address.NULL_ADDRESS ? validator.RewardAddress : null;
                }
                else
                {
                    Model.Status = "Disabled";
                    Model.ActionText = "Enable Validator";
                    Model.RewardAddress = null;
                }

                Model.Votes = votes;
                Model.AccumulatedReward = estimatedReward;
                Model.NextEpoch = endOfEpoch;
                Model.SetBalance(validator?.Stake ?? 0UL);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async void SetValidatorState(object? sender, RoutedEventArgs args)
    {
        if(TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        if (Model.Status == "Enabled")
        {
            var ok = await ConfirmDialog.Show("Disable validator and unlock stake?", false, window);

            if (!ok)
            {
                return;
            }

            SetValidatorState(TransactionType.DEREGISTER_VALIDATOR, Address.NULL_ADDRESS);
        }
        else
        {
            var recipient = await SelectRecipient();

            if (recipient == Address.NULL_ADDRESS)
            {
                Console.WriteLine("NULL_RECIPIENT (invalid address?)");
                return;
            }

            SetValidatorState(TransactionType.REGISTER_VALIDATOR, recipient);
        }
    }

    public async Task<Address> SelectRecipient()
    {        
        try
        {
            if(TopLevel.GetTopLevel(this) is not Window window)
            {
                return Address.NULL_ADDRESS;
            }

            using var scope = Program.ServiceCollection.CreateScope();
            var keyRepository = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var walletManager = scope.ServiceProvider.GetRequiredService<IWalletManager>();

            var key = keyRepository.GetKey();

            var title = "Select address for stake rewards";
            var description = "Address for stake rewards";
            var address = await InputDialog.Show(title, description, window);

            if (address is null)
            {
                return Address.NULL_ADDRESS;
            }

            if (!Address.IsValid(address))
            {
                return Address.NULL_ADDRESS;
            }

            return address;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return Address.NULL_ADDRESS;
        }
    }

    public async void ReturnFunds(object? sender, RoutedEventArgs args)
    {
        try
        {
            if(TopLevel.GetTopLevel(this) is not Window window)
            {
                return;
            }

            using var scope = Program.ServiceCollection.CreateScope();
            var keyRepository = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var walletManager = scope.ServiceProvider.GetRequiredService<IWalletManager>();

            var key = keyRepository.GetKey();
            var stake = storeManager.GetStake(key.Address);

            var model = new TransferModel
            {
                From = key.Address.ToString(),
                Min = 0,
                Max = stake?.Stake ?? 0UL,
                Amount = "0",
                RecipientDescription = "Recipient"
            };

            var result = await SetStakeDialog.Show("Return funds", model, window);

            if (result is null || result.To is null)
            {
                return;
            }

            if (!decimal.TryParse(result.Amount, out var amount))
            {
                return;
            }

            if (!Address.IsValid(result.To))
            {
                return;
            }

            if (amount <= 0)
            {
                return;
            }

            var tx = new Transaction
            {
                TransactionType = TransactionType.PAYMENT,
                PublicKey = key.PublicKey,
                To = result.To,
                Value = (ulong)(amount * Constant.DECIMAL_MULTIPLIER),
                Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };

            tx.Sign(key.PrivateKey);

            Console.WriteLine(storeManager.AddTransaction(new TransactionDto(tx), true));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private async void SetValidatorState(TransactionType transactionType, Address rewardRecipient)
    {
        try
        {
            using var scope = Program.ServiceCollection.CreateScope();
            var keyRepository = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var key = keyRepository.GetKey();

            var tx = new Transaction
            {
                TransactionType = transactionType,
                PublicKey = key.PublicKey,
                To = rewardRecipient,
                Value = 0,
                Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };

            tx.Sign(key.PrivateKey);

            var result = storeManager.AddTransaction(new TransactionDto(tx), true);

            if(TopLevel.GetTopLevel(this) is not Window window)
            {
                return;
            }

            await ConfirmDialog.Show($"Transaction Status: {result}", true, window);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}

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
        using var scope = Program.ServiceCollection.CreateScope();
        var keyRepository = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        var key = keyRepository.GetKey();
        var validator = storeManager.GetStake(key.Address);
        var ledger = storeManager.GetLedger(key.Address);

        var votes = storeManager.GetVotesForAddress(key.Address, 6).Select(x => new TransactionModel
        {
            Recipient = x.To,
            Amount = (long)x.Value,
            Timestamp = x.Timestamp
        }).ToList();

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
                Model.Locked = validator.Stake;
                Model.Available = ledger?.Balance ?? 0UL;
                Model.Total = Model.Locked + Model.Available;
                Model.Votes = votes;
            }
            else
            {
                Model.Status = "Disabled";
                Model.ActionText = "Enable Validator";
                Model.RewardAddress = null;
                Model.Locked = 0;
                Model.Available = ledger?.Balance ?? 0UL;
                Model.Total = Model.Locked + Model.Available;
                Model.Votes = votes;
            }
        });
    }

    public void SetValidatorState(object? sender, RoutedEventArgs args)
    {
        if (Model.Status == "Enabled")
        {
            DisableValidator();
        }
        else
        {
            SetStake(sender, args);
        }
    }

    public async void SetStake(object? sender, RoutedEventArgs args)
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

            var model = new TransferModel
            {
                From = key.Address.ToString(),
                To = Model.RewardAddress?.ToString() ?? null,
                Min = Constant.MIN_STAKE,
                Max = Model.Total,
                Amount = (Model.Locked / Constant.DECIMAL_MULTIPLIER).ToString(),
                RecipientDescription = "Stake reward address"
            };

            var title = Model.Status == "Disabled" ? "Set Stake" : "Update Stake";
            var result = await SetStakeDialog.Show(title, model, window);

            if (result is null || result.To is null)
            {
                return;
            }

            if (!decimal.TryParse(result.Amount, out var amount))
            {
                return;
            }

            if (result.To == Model.RewardAddress && amount == Model.Locked)
            {
                return;
            }

            if (!Address.IsValid(result.To))
            {
                return;
            }

            if ((amount * Constant.DECIMAL_MULTIPLIER) < Constant.MIN_STAKE)
            {
                return;
            }

            var tx = new Transaction
            {
                TransactionType = TransactionType.REG_VALIDATOR,
                PublicKey = key.PublicKey,
                To = result.To,
                Value = (ulong)(amount * Constant.DECIMAL_MULTIPLIER),
                Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };

            tx.Sign(key.PrivateKey);

            storeManager.AddTransaction(new TransactionDto(tx), true);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
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

            var model = new TransferModel
            {
                From = key.Address.ToString(),
                Min = 0,
                Max = Model.Available,
                Amount = (Model.Available / Constant.DECIMAL_MULTIPLIER).ToString(),
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

            storeManager.AddTransaction(new TransactionDto(tx), true);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private async void DisableValidator()
    {
        try
        {
            if(TopLevel.GetTopLevel(this) is not Window window)
            {
                return;
            }

            var result = await ConfirmDialog.Show("Disable validator and unlock stake?", window);

            if (!result)
            {
                return;
            }

            using var scope = Program.ServiceCollection.CreateScope();
            var keyRepository = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var key = keyRepository.GetKey();

            var tx = new Transaction
            {
                TransactionType = TransactionType.REG_VALIDATOR,
                PublicKey = key.PublicKey,
                Value = 0,
                Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };

            tx.Sign(key.PrivateKey);

            storeManager.AddTransaction(new TransactionDto(tx), true);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}

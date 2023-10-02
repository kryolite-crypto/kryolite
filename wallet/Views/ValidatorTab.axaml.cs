using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kryolite.Wallet;

public partial class ValidatorTab : UserControl
{
    // private OverviewTabViewModel Model = new();

    public ValidatorTab()
    {
        AvaloniaXamlLoader.Load(this);
        //DataContext = Model;
    }
}

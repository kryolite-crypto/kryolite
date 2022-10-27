using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace holvi_wallet;

public partial class OverviewTab : UserControl
{
    private OverviewTabViewModel Model = new();

    public OverviewTab()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = Model;
    }
}

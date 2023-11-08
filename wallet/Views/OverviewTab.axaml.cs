using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Kryolite.Wallet;

public partial class OverviewTab : UserControl
{
    private OverviewTabViewModel Model;

    public OverviewTab()
    {
        AvaloniaXamlLoader.Load(this);
        Model = new();
        DataContext = Model;
    }
}

using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Wallet;

public class GroupBox : HeaderedContentControl
{
    static GroupBox()
    {
        FocusableProperty.OverrideMetadata<GroupBox>(new StyledPropertyMetadata<bool>(false));
        KeyboardNavigation.TabNavigationProperty.OverrideMetadata<GroupBox>
            (new StyledPropertyMetadata<KeyboardNavigationMode>(KeyboardNavigationMode.None));
    }
}

﻿using Kryolite.Shared;

namespace Kryolite.Node;

public class ApprovalEventArgs : EventArgs
{
    public Address From { get; set; } = new();
    public Address To { get; set; } = new();
    public SHA256Hash TokenId { get; set; } = new();
}

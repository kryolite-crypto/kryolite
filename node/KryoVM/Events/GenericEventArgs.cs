using Kryolite.EventBus;

public class GenericEventArgs : EventBase
{
    public string Json { get; set; } = string.Empty;
}

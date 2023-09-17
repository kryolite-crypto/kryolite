namespace Kryolite.EventBus;

public class Subscription<TEvent> : ISubscription where TEvent : EventBase
{
    public Guid SubscriptionId { get; }

    private Action<TEvent> Action { get; }

    public Subscription(Action<TEvent> action)
    {
        SubscriptionId = Guid.NewGuid();
        Action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Publish(EventBase ev)
    {
        if (ev is not TEvent eventBase)
        {
            throw new ArgumentException();
        }

        Action.Invoke(eventBase);
    }
}

namespace Kryolite.EventBus;

public class Subscription<TEvent> : IDisposable, ISubscription where TEvent : EventBase
{
    public Guid SubscriptionId { get; }

    private Action<TEvent> Action { get; }
    
    private readonly EventBus _eventBus;

    public Subscription(Action<TEvent> action, EventBus eventBus)
    {
        SubscriptionId = Guid.NewGuid();
        Action = action ?? throw new ArgumentNullException(nameof(action));
        _eventBus = eventBus;
    }

    public void Publish(EventBase ev)
    {
        if (ev is not TEvent eventBase)
        {
            throw new ArgumentException();
        }

        Action.Invoke(eventBase);
    }

    public void Dispose()
    {
        _eventBus.Unsubscribe(SubscriptionId);
    }
}

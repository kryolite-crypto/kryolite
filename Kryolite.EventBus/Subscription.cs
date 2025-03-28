namespace Kryolite.EventBus;

public class Subscription<TEvent> : IDisposable, ISubscription where TEvent : IEvent
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

    public void Publish(IEvent ev)
    {
        Action.Invoke((TEvent)ev);
    }

    public void Dispose()
    {
        _eventBus.Unsubscribe(SubscriptionId);
    }
}

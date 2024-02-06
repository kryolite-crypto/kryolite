namespace Kryolite.EventBus;

public class EventBus : IEventBus
{
    private Dictionary<Type, List<ISubscription>> Subscriptions { get; }

    public EventBus()
    {
        Subscriptions = new ();
    }

    public Guid Subscribe<TEvent>(Action<TEvent> action) where TEvent : EventBase
    {
        var type = typeof(TEvent);

        lock (_lock)
        {
            var sub = new Subscription<TEvent>(action);

            if (!Subscriptions.ContainsKey(type))
            {
                Subscriptions.Add(type, new List<ISubscription>());
            }

            Subscriptions[typeof(TEvent)].Add(sub);

            return sub.SubscriptionId;
        }
    }

    public void Unsubscribe(Guid subscriptionId)
    {
        lock (_lock)
        {
            foreach (var subs in Subscriptions)
            {
                var toRemove = subs.Value.FirstOrDefault(x => x.SubscriptionId == subscriptionId);
                
                if (toRemove is not null)
                {
                    subs.Value.Remove(toRemove);
                }
            }
        }
    }

    public async Task Publish<TEvent>(TEvent ev) where TEvent : EventBase
    {
        var subs = new List<ISubscription>();

        lock (_lock)
        {
            if (Subscriptions.ContainsKey(ev.GetType()))
            {
                subs = [.. Subscriptions[ev.GetType()]];
            }
        }

        foreach (var sub in subs)
        {
            await Task.Run(() => sub.Publish(ev));
        }
    }

    public async Task Publish<TEvent>(List<TEvent> events) where TEvent : EventBase
    {
        foreach (var ev in events)
        {
            await Publish(ev);
        }
    }

    private static readonly object _lock = new object();
}

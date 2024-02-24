namespace Kryolite.EventBus;

public interface ISubscription
{
    public Guid SubscriptionId { get; }
    void Publish(EventBase ev);
}

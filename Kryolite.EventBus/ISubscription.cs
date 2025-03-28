namespace Kryolite.EventBus;

public interface ISubscription : IDisposable
{
    public Guid SubscriptionId { get; }
    void Publish(IEvent ev);
}

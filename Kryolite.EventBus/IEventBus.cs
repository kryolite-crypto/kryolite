namespace Kryolite.EventBus;

public interface IEventBus
{
    ISubscription Subscribe<TEvent>(Action<TEvent> action) where TEvent : IEvent;
    void Unsubscribe(Guid subscriptionId);
    Task Publish<TEvent>(TEvent ev) where TEvent : IEvent;
    Task Publish<TEvent>(List<TEvent> events) where TEvent : IEvent;
}

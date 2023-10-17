namespace Kryolite.EventBus;

public interface IEventBus
{
    Guid Subscribe<TEvent>(Action<TEvent> action) where TEvent : EventBase;
    void Unsubscribe(Guid subscriptionId);
    Task Publish<TEvent>(TEvent ev) where TEvent : EventBase;
    Task Publish<TEvent>(List<TEvent> events) where TEvent : EventBase;
}

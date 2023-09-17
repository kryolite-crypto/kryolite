namespace Kryolite.EventBus;

public interface IEventBus
{
    Guid Subscribe<TEvent>(Action<TEvent> action) where TEvent : EventBase;
    void Unsubscribe(Guid subscriptionId);
    void Publish<TEvent>(TEvent ev) where TEvent : EventBase;
}

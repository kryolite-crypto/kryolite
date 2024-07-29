namespace Kryolite.EventBus.Tests;

public class EventBusTests
{
    private readonly EventBus eventBus;

    public EventBusTests()
    {
        eventBus = new EventBus();
    }

    [Fact]
    public void Subscribe_AddsSubscription()
    {
        var subscription = eventBus.Subscribe<TestEvent>(e => { });

        Assert.NotNull(subscription);
        Assert.Single(eventBus.Subscriptions[typeof(TestEvent)]);
    }

    [Fact]
    public void Subscribe_MultipleEventTypes()
    {
        eventBus.Subscribe<TestEvent>(e => { });
        eventBus.Subscribe<AnotherTestEvent>(e => { });

        Assert.Single(eventBus.Subscriptions[typeof(TestEvent)]);
        Assert.Single(eventBus.Subscriptions[typeof(AnotherTestEvent)]);
    }

    [Fact]
    public void Unsubscribe_RemovesSubscription()
    {
        var subscription = eventBus.Subscribe<TestEvent>(e => { });
        eventBus.Unsubscribe(subscription.SubscriptionId);

        Assert.Empty(eventBus.Subscriptions[typeof(TestEvent)]);
    }

    [Fact]
    public void Unsubscribe_NonExistentSubscription()
    {
        eventBus.Subscribe<TestEvent>(e => { });
        var initialCount = eventBus.Subscriptions[typeof(TestEvent)].Count;

        eventBus.Unsubscribe(Guid.NewGuid());

        Assert.Equal(initialCount, eventBus.Subscriptions[typeof(TestEvent)].Count);
    }

    [Fact]
    public async Task Publish_CallsSubscribedAction()
    {
        var called = false;
        eventBus.Subscribe<TestEvent>(e => called = true);

        await eventBus.Publish(new TestEvent());

        Assert.True(called);
    }

    [Fact]
    public async Task Publish_MultipleEvents_CallsSubscribedActions()
    {
        var callCount = 0;
        eventBus.Subscribe<TestEvent>(e => callCount++);
        eventBus.Subscribe<AnotherTestEvent>(e => callCount++);

        await eventBus.Publish(new TestEvent());
        await eventBus.Publish(new AnotherTestEvent());

        Assert.Equal(2, callCount);
    }

    // Event classes for testing
    public class TestEvent : EventBase { }
    public class AnotherTestEvent : EventBase { }
}

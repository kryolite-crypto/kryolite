using System.Collections.ObjectModel;
using Redbus;
using Redbus.Configuration;
using Redbus.Events;
using Redbus.Interfaces;

namespace Kryolite.Redbus
{
    /// <summary>
    /// Implements <see cref="IEventBus"/>.
    /// </summary>
    public class EventBus : IEventBus
    {
        private readonly IEventBusConfiguration _eventBusConfiguration;
        public EventBus(IEventBusConfiguration configuration = null!)
        {
            _eventBusConfiguration = configuration ?? new EventBusConfiguration();
            _subscriptions = new Dictionary<Type, List<ISubscription>>();
        }

        /// <summary>
        /// Subscribes to the specified event type with the specified action
        /// </summary>
        /// <typeparam name="TEventBase">The type of event</typeparam>
        /// <param name="action">The Action to invoke when an event of this type is published</param>
        /// <returns>A <see cref="SubscriptionToken"/> to be used when calling <see cref="Unsubscribe"/></returns>
        public SubscriptionToken Subscribe<TEventBase>(Action<TEventBase> action) where TEventBase : EventBase
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            lock (SubscriptionsLock)
            {
                if (!_subscriptions.ContainsKey(typeof(TEventBase)))
                    _subscriptions.Add(typeof(TEventBase), new List<ISubscription>());

                _subscriptions[typeof(TEventBase)].Add(new Subscription<TEventBase>(action, null!));
                return null!;
            }
        }

        /// <summary>
        /// Unsubscribe from the Event type related to the specified <see cref="SubscriptionToken"/>
        /// </summary>
        /// <param name="token">The <see cref="SubscriptionToken"/> received from calling the Subscribe method</param>
        public void Unsubscribe(SubscriptionToken token)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            lock (SubscriptionsLock)
            {
                if (_subscriptions.ContainsKey(token.EventItemType))
                {
                    var allSubscriptions = _subscriptions[token.EventItemType];
                    var subscriptionToRemove = allSubscriptions.FirstOrDefault(x => x.SubscriptionToken.Token == token.Token);
                    if (subscriptionToRemove != null)
                        _subscriptions[token.EventItemType].Remove(subscriptionToRemove);
                }
            }
        }

        /// <summary>
        /// Publishes the specified event to any subscribers for the <see cref="TEventBase"/> event type
        /// </summary>
        /// <typeparam name="TEventBase">The type of event</typeparam>
        /// <param name="eventItem">Event to publish</param>
        public void Publish<TEventBase>(TEventBase eventItem) where TEventBase : EventBase
        {
            if (eventItem == null)
                throw new ArgumentNullException(nameof(eventItem));

            var allSubscriptions = new List<ISubscription>();
            lock (SubscriptionsLock)
            {
                if (_subscriptions.ContainsKey(eventItem.GetType()))
                    allSubscriptions = _subscriptions[eventItem.GetType()].ToList();
            }

            for (var index = 0; index < allSubscriptions.Count; index++)
            {
                var subscription = allSubscriptions[index];
                try
                {
                    subscription.Publish(eventItem);
                }
                catch (Exception)
                {
                    if (_eventBusConfiguration.ThrowSubscriberException)
                        throw;
                }
            }
        }

        /// <summary>
        /// Publishes the specified event to any subscribers for the <see cref="TEventBase"/> event type asychronously
        /// </summary>
        /// <remarks> This is a wrapper call around the synchronous  method as this method is naturally synchronous (CPU Bound) </remarks>
        /// <typeparam name="TEventBase">The type of event</typeparam>
        /// <param name="eventItem">Event to publish</param>
        public void PublishAsync<TEventBase>(TEventBase eventItem) where TEventBase : EventBase
        {
            PublishAsyncInternal(eventItem, null!);
        }

        /// <summary>
        /// Publishes the specified event to any subscribers for the <see cref="TEventBase"/> event type asychronously
        /// </summary>
        /// <remarks> This is a wrapper call around the synchronous  method as this method is naturally synchronous (CPU Bound) </remarks>
        /// <typeparam name="TEventBase">The type of event</typeparam>
        /// <param name="eventItem">Event to publish</param>
        /// <param name="callback"><see cref="AsyncCallback"/> that is called on completion</param>
        public void PublishAsync<TEventBase>(TEventBase eventItem, AsyncCallback callback) where TEventBase : EventBase
        {
            PublishAsyncInternal(eventItem, callback);
        }

        #region PRIVATE METHODS
        private void PublishAsyncInternal<TEventBase>(TEventBase eventItem, AsyncCallback callback) where TEventBase : EventBase
        {
            Task<bool> publishTask = new Task<bool>(() =>
            {
                Publish(eventItem);
                return true;
            });
            publishTask.Start();
            if (callback == null)
                return;

            var tcs = new TaskCompletionSource<bool>();
            publishTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    tcs.TrySetException(t.Exception?.InnerExceptions ?? new ReadOnlyCollection<Exception>(new List<Exception> { new Exception ("Unknown exception")}));
                else if (t.IsCanceled)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(t.Result);
                callback?.Invoke(tcs.Task);
            }, TaskScheduler.Default);
        }

        #endregion

        private readonly Dictionary<Type, List<ISubscription>> _subscriptions;
        private static readonly object SubscriptionsLock = new object();
    }

    internal class Subscription<TEventBase> : ISubscription where TEventBase : EventBase
    {
        public SubscriptionToken SubscriptionToken { get; }

        public Subscription(Action<TEventBase> action, SubscriptionToken token)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
            SubscriptionToken = token;
        }

        public void Publish(EventBase eventItem)
        {
            if (eventItem is not TEventBase ev)
                throw new ArgumentException("Event Item is not the correct type.");

            _action.Invoke(ev);
        }

        private readonly Action<TEventBase> _action;
    }
}

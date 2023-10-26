using System.Collections.Concurrent;
using LocklessQueue.Queues;

namespace Snowjak.SimpleMessageBus;

/// <summary>
/// Provides *basic* intra-application pub-sub messaging.
/// <para>
/// A MessageBus distributes messages from publishers to subscribers:
/// <list type="bullet">
/// <item>All messages are typed, extending from either <see cref="IMessage"/> or <see cref="IPoolableMessage"/>.</item>
/// <item>Subscribers listen for message-types. A subscription may be configured to receive only messages whose type matches
/// exactly, or for messages of any subtype.</item>
/// <item>Subscribers may receive messages on the same thread as the publisher (i.e., synchronous delivery),
/// or on a separate worker-thread.</item>
/// <item>If desired, subscribers may listen for messages on a specific Topic (which can be any <c>object</c>).</item>
/// </list>
/// </para>
/// </summary>
public class MessageBus
{
    private static readonly Mutex SingletonLock = new();
    private static MessageBus? _shared = null;
    public static MessageBus Shared
    {
        get
        {
            if (_shared == null)
            {
                SingletonLock.WaitOne();
                _shared ??= new();
                SingletonLock.ReleaseMutex();
            }
            return _shared;
        }
    }

    /// <summary>
    /// Encapsulates a <see cref="MessageBus"/>'s settings. 
    /// </summary>
    public sealed class MessageBusSettings
    {
        /// <summary>
        /// For any single <see cref="IPoolableMessage"/> type, we will have, at most,
        /// so-many objects in that type's object-pool. (by default, 64)
        /// </summary>
        public uint ObjectPoolSize { get; set; } = 64;
    }
    private readonly MessageBusSettings Settings = new();

    /// <summary>
    /// Construct a new MessageBus with default settings.
    /// </summary>
    public MessageBus() { }

    /// <summary>
    /// Construct a new MessageBus and use the given <paramref name="SetupAction"/> to modify
    /// its configuration.
    /// </summary>
    /// <param name="SetupAction"></param>
    public MessageBus(Action<MessageBusSettings> SetupAction) => SetupAction(Settings);

    private readonly Dictionary<object, List<Subscription>> namedTopics = new();
    private readonly List<Subscription> anyTopics = new();

    private readonly ConcurrentDictionary<object, Mutex> namedLocks = new();
    private readonly Mutex anyLock = new();

    private readonly List<Subscription> emptyList = new();
    private readonly ConcurrentDictionary<Type, SPSCQueue<IPoolableMessage>> messagePools = new();

    /// <summary>
    /// Get a new instance of <typeparamref name="M"/> from the internal pool,
    /// or create a new one if the pool is empty.
    /// </summary>
    /// <typeparam name="M"></typeparam>
    /// <returns></returns>
    internal M Get<M>() where M : IPoolableMessage
    {
        if (messagePools.GetOrAdd(typeof(M), _ => new((int)Settings.ObjectPoolSize))
            .TryDequeue(out var instance))
            return (M)instance;
        return Activator.CreateInstance<M>();
    }

    /// <summary>
    /// Return the given object-instance to the pool.
    /// </summary>
    /// <typeparam name="M"></typeparam>
    /// <param name="instance"></param>
    internal void Retire<M>(M instance) where M : IPoolableMessage
    {
        instance.Reset();
        messagePools.GetOrAdd(typeof(M), _ => new((int)Settings.ObjectPoolSize))
            .TryEnqueue(instance);
    }

    /// <summary>
    /// Retrieve a <see cref="IPoolableMessage"/> from the relevant pool, configure it using <paramref name="MessageConfigurer"/>,
    /// and publish it to all Topics.
    /// </summary>
    /// <typeparam name="M"></typeparam>
    /// <param name="MessageConfigurer"></param>
    public void Publish<M>(Action<M> MessageConfigurer) where M : IPoolableMessage => PublishPoolable(null, MessageConfigurer);

    /// <summary>
    /// Retrieve a <see cref="IPoolableMessage"/> from the relevant pool, configure it using <paramref name="MessageConfigurer"/>,
    /// and publish it to the given <paramref name="Topic"/>.
    /// </summary>
    /// <typeparam name="M"></typeparam>
    /// <param name="Topic"></param>
    /// <param name="MessageConfigurer"></param>
    public void Publish<M>(object Topic, Action<M> MessageConfigurer) where M : IPoolableMessage => PublishPoolable(Topic, MessageConfigurer);

    private void PublishPoolable<M>(object? Topic, Action<M> MessageConfigurer) where M : IPoolableMessage
    {
        var m = Get<M>();
        MessageConfigurer(m);
        PublishMessage(Topic, m);
    }

    /// <summary>
    /// Publish a new <paramref name="Message"/> to all Topics.
    /// </summary>
    /// <typeparam name="M"></typeparam>
    /// <param name="Message"></param>
    public void Publish<M>(M Message) where M : IMessage => PublishMessage(null, Message);
    /// <summary>
    /// Publish a new <paramref name="Message"/> to the given <paramref name="Topic"/>.
    /// </summary>
    /// <typeparam name="M"></typeparam>
    /// <param name="Topic"></param>
    /// <param name="Message"></param>
    public void Publish<M>(object Topic, M Message) where M : IMessage => PublishMessage(Topic, Message);

    private void PublishMessage<M>(object? Topic, M Message) where M : IBaseMessage
    {
        if (Topic != default)
        {
            namedLocks.GetValueOrDefault(Topic)?.WaitOne();
            foreach (var subscription in namedTopics.GetValueOrDefault(Topic) ?? emptyList)
                subscription.Offer(Message);
            namedLocks.GetValueOrDefault(Topic)?.ReleaseMutex();
        }

        anyLock.WaitOne();
        foreach (var subscription in anyTopics)
            subscription.Offer(Message);
        anyLock.ReleaseMutex();

        if (Message is IPoolableMessage pm)
            Retire(pm);
    }

    /// <summary>
    /// Start building a new subscription, accepting messages
    /// that can be assigned to the given type -- i.e., messages that
    /// either match, or are subclasses of, <typeparamref name="M"/>.
    /// <para>
    /// If <paramref name="AlsoSubclasses"/> is <c>true</c> (default), then this subscription will receive both
    /// messages of type <typeparamref name="M"/> *and* also all subclasses of <typeparamref name="M"/>.
    /// If <c>false</c>, then the type must match exactly.
    /// </para>
    /// </summary>
    public FromBuilder<M> When<M>(bool AlsoSubclasses = true) where M : IBaseMessage => new(this, AlsoSubclasses);

    /// <summary>
    /// Start building a new subscription.
    /// <para>
    /// If <paramref name="AlsoSubclasses"/> is <c>true</c> (default), then this subscription will receive both
    /// messages of type <typeparamref name="M"/> *and* also all subclasses of <typeparamref name="M"/>.
    /// If <c>false</c>, then the type must match exactly.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// If <paramref name="messageType"/> is not an implementation of <see cref="IBaseMessage"/>
    /// </exception>
    public FromBuilder<IBaseMessage> When(Type messageType, bool AlsoSubclasses = true)
    {
        if (!messageType.IsAssignableTo(typeof(IBaseMessage)))
            throw new ArgumentException($"Given type [{messageType.Name}] is not an implementation of {typeof(IBaseMessage).Name}.");
        return new FromBuilder<IBaseMessage>(this, messageType, AlsoSubclasses);
    }
    /// <summary>
    /// Start building a new subscription, accepting messages of any type.
    /// </summary>
    public FromBuilder<IBaseMessage> WhenAny() => new(this, true);

    public class FromBuilder<M> where M : IBaseMessage
    {
        private readonly MessageBus _bus;
        internal readonly Type messageType;
        internal readonly bool alsoSubclasses;
        internal object? Topic = default;

        internal FromBuilder(MessageBus bus, bool alsoSubclasses)
        {
            _bus = bus;
            messageType = typeof(M);
            this.alsoSubclasses = alsoSubclasses;
        }
        internal FromBuilder(MessageBus bus, Type messageType, bool alsoSubclasses)
        {
            _bus = bus;
            this.messageType = messageType;
            this.alsoSubclasses = alsoSubclasses;
        }

        /// <summary>
        /// Continue building a new subscription, accepting messages from the given
        /// topic-name.
        /// </summary>
        public ThenBuilder<M> From(object Topic)
        {
            this.Topic = Topic ?? default;
            return new(_bus, this);
        }

        /// <summary>
        /// Continue building a new subscription, accepting messages from any topic.
        /// </summary>
        public ThenBuilder<M> FromAny() => new(_bus, this);
    }

    public class ThenBuilder<M> where M : IBaseMessage
    {
        private readonly MessageBus _bus;
        private readonly FromBuilder<M> _from;

        internal ThenBuilder(MessageBus bus, FromBuilder<M> from)
        {
            _bus = bus;
            _from = from;
        }

        /// <summary>
        /// Finish building the subscription, describing the action that consume the
        /// incoming message.
        /// <para>
        /// If <paramref name="Synchronous"/> is <c>true</c> (default), then this Consumer *must* receive messages
        /// synchronously with (i.e., in the same thread as) the Sender.
        /// If <c>false</c>, then this Consumer will receive messages on a different thread.
        /// </para>
        /// </summary>
        public UnsubscribeHandle Then(Action<M> Consumer, bool Synchronous = true)
        {
            Subscription subscription;
            if (typeof(M) == typeof(IBaseMessage))
                subscription = Subscription.Create((Consumer as Action<IBaseMessage>)!, _from.messageType, Synchronous, _from.alsoSubclasses);
            else
                subscription = Subscription.Create(Consumer, Synchronous, _from.alsoSubclasses);

            if (_from.Topic == default)
            {
                _bus.anyLock.WaitOne();
                _bus.anyTopics.Add(subscription);
                _bus.anyLock.ReleaseMutex();
            }
            else
            {
                _bus.namedLocks.GetValueOrDefault(_from.Topic)?.WaitOne();
                if (!_bus.namedTopics.ContainsKey(_from.Topic))
                    _bus.namedTopics.Add(_from.Topic, new());
                _bus.namedTopics[_from.Topic].Add(subscription);
                _bus.namedLocks.GetValueOrDefault(_from.Topic)?.ReleaseMutex();
            }

            return new UnsubscribeHandle(_bus, _from.Topic, subscription);
        }
    }

    /// <summary>
    /// Allows the holder to unsubscribe from an existing MessageBus subscription.
    /// </summary>
    public sealed class UnsubscribeHandle
    {
        private readonly MessageBus bus;
        private readonly object? topic;
        private readonly Subscription subscription;
        public UnsubscribeHandle(MessageBus bus, object? topic, Subscription subscription)
        {
            this.bus = bus;
            this.topic = topic;
            this.subscription = subscription;
        }

        public void Unsubscribe()
        {
            if (topic != null)
            {
                bus.namedLocks.GetValueOrDefault(topic)?.WaitOne();
                bus.namedTopics.GetValueOrDefault(topic)?.Remove(subscription);
                bus.namedLocks.GetValueOrDefault(topic)?.ReleaseMutex();
            }
            else
            {
                bus.anyLock.WaitOne();
                bus.anyTopics.Remove(subscription);
                bus.anyLock.ReleaseMutex();
            }
        }
    }
}

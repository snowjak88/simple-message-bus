namespace org.tanplan.hivemind.util;

/// <summary>
/// Describes a message-consumer.
/// </summary>
public sealed class Subscription
{
    /// <summary>
    /// Create a new Subscription, receiving messages of the given <paramref name="MessageType"/> (and its subtypes) using
    /// the given <paramref name="Consumer"/>.
    /// <para>
    /// If <paramref name="Synchronous"/> is <c>true</c>, then <paramref name="Consumer"/> will be called
    /// on the same thread as the message-sender. Otherwise, it will be invoked on a worker-thread.
    /// </para>
    /// <para>
    /// If <paramref name="AlsoSubclasses"/> is <c>true</c>, then <paramref name="Consumer"/> will receive
    /// both instances of <paramref name="MessageType"/> *and* all its subclasses. Otherwise, it will receive
    /// *only* instances of <paramref name="MessageType"/>.
    /// </para>
    /// </summary>
    /// <param name="Consumer"></param>
    /// <param name="MessageType"></param>
    /// <param name="Synchronous"></param>
    /// <param name="AlsoSubclasses"></param>
    /// <returns></returns>
    public static Subscription Create(Action<IBaseMessage> Consumer, Type MessageType, bool Synchronous, bool AlsoSubclasses) =>
        new(MessageType,
        message =>
        {
            if (message.GetType().IsAssignableTo(MessageType))
                Consumer(message);
        }
        , Synchronous, AlsoSubclasses);

    /// <summary>
    /// Create a new Subscription, receiving messages of the given <typeparamref name="M"/> (and its subtypes) using
    /// the given <paramref name="Consumer"/>.
    /// <para>
    /// If <paramref name="Synchronous"/> is <c>true</c>, then <paramref name="Consumer"/> will be called
    /// on the same thread as the message-sender. Otherwise, it will be invoked on a worker-thread.
    /// </para>
    /// <para>
    /// If <paramref name="AlsoSubclasses"/> is <c>true</c>, then <paramref name="Consumer"/> will receive
    /// both instances of <paramref name="MessageType"/> *and* all its subclasses. Otherwise, it will receive
    /// *only* instances of <paramref name="MessageType"/>.
    /// </para>
    /// </summary>
    /// <param name="Consumer"></param>
    /// <param name="MessageType"></param>
    /// <param name="Synchronous"></param>
    /// <returns></returns>
    public static Subscription Create<M>(Action<M> Consumer, bool Synchronous, bool AlsoSubclasses) where M : IBaseMessage =>
        new(typeof(M),
        message =>
        {
            if (message is M m)
                Consumer(m);
        }
        , Synchronous, AlsoSubclasses);

    internal Subscription(Type messageType, Action<IBaseMessage> consumer, bool synchronous, bool alsoSubclasses)
    {
        this.messageType = messageType;
        this.consumer = consumer;
        this.synchronous = synchronous;
        this.alsoSubclasses = alsoSubclasses;
    }
    private readonly Type messageType;
    private readonly Action<IBaseMessage> consumer;
    private readonly bool synchronous;
    private readonly bool alsoSubclasses;

    /// <summary>
    /// Offer the given message to this Subscription. If <typeparamref name="M"/> is
    /// assignable to this Subscription's configured message-type, then <paramref name="Message"/>
    /// will be ingested.
    /// </summary>
    /// <typeparam name="M"></typeparam>
    /// <param name="Message"></param>
    public void Offer<M>(M Message) where M : IBaseMessage
    {
        if ((typeof(M) == messageType) || (alsoSubclasses && typeof(M).IsAssignableTo(messageType)))
            if (synchronous)
                consumer(Message);
            else
                Task.Run(() => consumer(Message));
    }
}

namespace org.tanplan.hivemind.util;

/// <summary>
/// Base type for all MessageBus messages.
/// </summary>
public interface IBaseMessage { }

/// <summary>
/// Base type for all MessageBus messages that will be created explicitly,
/// via their constructors.
/// </summary>
public interface IMessage : IBaseMessage { }

/// <summary>
/// Base type for all MessageBus messages that support object-pooling.
/// These should *not* be created explicitly, but instead obtained via
/// <see cref="MessageBus.Publish{M}(Action{M})"/> or
/// <see cref="MessageBus.Publish{M}(object, Action{M})"/>.
/// </summary>
public interface IPoolableMessage : IBaseMessage
{
    /// <summary>
    /// Reset this message to its "base" value. This is called automatically when this
    /// message is cycled back into the MessageBus's pool.
    /// </summary>
    public void Reset();
}
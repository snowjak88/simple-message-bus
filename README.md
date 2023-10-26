# SimpleMessageBus

Offers simple intra-app pub-sub messaging.

---

I needed a quick-and-dirty message-bus, and found a dearth of suitable NuGet libraries available.
I just needed a simple, in-memory message-bus.

* No persistent queues
* No dead-letter handling
* No distributed-processing capability

This project was created to answer that need.

## Sample Usage

### Basic Usage
```csharp
public class MyMessage : IMessage { }

//
// Subscribe to the shared (singleton) MessageBus instance,
// receiving messages of a given type.
//
// Messages are delivered synchronously (i.e., on the same
// thread as the publisher).
//
MessageBus.When<MyMessage>()
    .FromAny()
    .Then(m => Console.WriteLine("Received a message!"));

//
// Publish a message.
//
MessageBus.Shared.Publish(new MyMessage());
```

### Receive Only Matching Message-Types
```csharp
public class GeneralMessage : IMessage { }
public class SpecificMessage : GeneralMessage { }

//
// Subscribe to the shared MessageBus,
// receiving messages *exactly* of the given type.
//
MessageBus.When<GeneralMessage>(AllowSubclasses: false)
    .FromAny()
    .Then(m => Console.WriteLine("..."));

//
// This will be ignored.
MessageBus.Shared.Publish(new SpecificMessage());
//
// This will be received.
MessageBus.Shared.Publish(new GeneralMessage());
```

### Publish/Subscribe to Specific Topic
```csharp
//
// Messages may be published to specific Topics,
// where a Topic is any object (not just a string).
//
public class MyMessage : IMessage { }

MessageBus.When<GeneralMessage>()
    .From("my-topic")
    .Then(m => Console.WriteLine("check!"));

//
// This will be ignored.
MessageBus.Shared.Publish(new MyMessage());
//
//This will be received.
MessageBus.Shared.Publish("my-topic", new MyMessage());
```

### Asynchronous Delivery
```csharp
public class MyMessage : IMessage { }

//
// Delivery is specified by the subscriber, not the publisher.
MessageBus.Shared.When<MyMessage>()
    .FromAny()
    .Then(m => Console.WriteLine("asynchronous!"), Synchronous: false);

//
// Publishing is performed same as always:
MessageBus.Shared.Publish(new MyMessage());
```

### Message Pooling
```csharp
//
// Message-types that extend IPoolableMessage can be
// pooled, allowing us to avoid garbage-collection.
// This may be useful when performing high-throughput,
// low-footprint messaging.
//
public class MyMessage : IPoolableMessage {
    public int Value { get; set; }
    //
    // All pooled messages must be able to "reset" themselves
    // to a blank/fresh state.
    public void Reset() => Value = 0;
}

MessageBus.When<MyMessage>()
    .FromAny()
    .Then(m => Console.WriteLine($"Value = {m.Value}"));

//
// We do not explicitly create pooled messages,
// but obtain an instance from the MessageBus.
MessageBus.Shared.Publish<MyMessage>(m => m.Value = 1);

//
// We can, of course, publish pooled messages to a topic.
MessageBus.Shared.Publish<MyMessage>("my-topic", m => m.Value = 1);
```

### Instance (Non-Singleton) MessageBus
```csharp
//
// We can create a new MessageBus either with default settings ...
var bus = new MessageBus();

//
// ... or with customized settings.
bus = new MessageBus(settings => {
    settings.ObjectPoolSize = 16;
});
```
namespace Snowjak.SimpleMessageBus.Tests;

public class SynchronousTests
{
    private readonly MessageBus bus = new MessageBus();

    public class MyMessage : IMessage { }
    public sealed class SubMessage1 : MyMessage { }
    public sealed class SubMessage2 : MyMessage { }

    public sealed class OtherMessage : IMessage { }

    [Test]
    public void TestSimplePublish()
    {
        var consumed = false;
        bus.When<MyMessage>().FromAny().Then(m => consumed = true);
        bus.Publish(new MyMessage());
        Assert.That(consumed, Is.True);
    }

    [Test]
    public void TestSimpleInheritedMessage()
    {
        var consumed = false;
        bus.When<MyMessage>().FromAny().Then(m => consumed = true);

        bus.Publish(new SubMessage1());
        Assert.That(consumed, Is.True);

        consumed = false;
        bus.Publish(new SubMessage2());
        Assert.That(consumed, Is.True);
    }

    [Test]
    public void TestTopicPublishing()
    {
        var consumed = false;

        var topic1 = "abc";
        var topic2 = 123;

        bus.When<MyMessage>().From(topic1).Then(m => consumed = true);

        bus.Publish(topic1, new MyMessage());
        Assert.That(consumed, Is.True);

        consumed = false;
        bus.Publish(topic2, new MyMessage());
        Assert.That(consumed, Is.False);

        consumed = false;
        bus.Publish(new MyMessage());
        Assert.That(consumed, Is.False);
    }

    [Test]
    public void TestTopicPublishing_FromAny()
    {
        var consumed = false;

        var topic = "abc";

        bus.When<MyMessage>().FromAny().Then(m => consumed = true);

        bus.Publish(topic, new MyMessage());
        Assert.That(consumed, Is.True);
    }

    [Test]
    public void TestPublishAfterUnsubscribe()
    {
        var consumed = false;
        var unsubscriber = bus.When<MyMessage>().FromAny().Then(m => consumed = true);

        bus.Publish(new MyMessage());
        Assert.That(consumed, Is.True);

        unsubscriber.Unsubscribe();

        consumed = false;
        bus.Publish(new MyMessage());
        Assert.That(consumed, Is.False);
    }
}
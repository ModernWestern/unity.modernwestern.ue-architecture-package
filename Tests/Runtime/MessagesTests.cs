using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.Tests
{
    public class MessagesTests
    {
        private readonly List<MessageListener> _listeners = new();

        [TearDown]
        public void StopListening()
        {
            foreach (var listener in _listeners)
            {
                listener.Dispose();
            }

            _listeners.Clear();
        }

        private void Keep(MessageListener listener)
        {
            _listeners.Add(listener);
        }

        [Test]
        public void Broadcast_ReachesEveryListener()
        {
            var received = new List<int>();

            Keep(Messages<TestMessage>.Listen(received, static (list, message) => list.Add(message.Value)));

            Keep(Messages<TestMessage>.Listen(received, static (list, message) => list.Add(message.Value * 10)));

            Messages.Broadcast(new TestMessage { Value = 2 });

            CollectionAssert.AreEquivalent(new[] { 2, 20 }, received);
        }

        [Test]
        public void MessageTypes_AreIndependent()
        {
            var counter = new Counter();

            Keep(Messages<TestMessage>.Listen(counter, static (c, _) => c.Calls++));

            Messages.Broadcast(new OtherMessage { Value = 1 });

            Assert.AreEqual(0, counter.Calls);
        }

        [Test]
        public void Dispose_StopsListening()
        {
            var counter = new Counter();

            var listener = Messages<TestMessage>.Listen(counter, static (c, m) => c.Sum += m.Value);

            Assert.IsTrue(listener.IsListening);

            listener.Dispose();

            Assert.IsFalse(listener.IsListening);

            Messages.Broadcast(new TestMessage { Value = 5 });

            Assert.AreEqual(0, counter.Sum);

            listener.Dispose();
        }

        [Test]
        public void ADestroyedUnityOwner_StopsListeningByItself()
        {
            var owner = new GameObject("Listener").transform;

            var calls = new Counter();

            Keep(Messages<TestMessage>.Listen(calls, static (c, _) => c.Calls++));

            var listener = Messages<TestMessage>.Listen(owner, static (t, _) => t.name = "Called");

            var before = Messages<TestMessage>.ListenerCount;

            Object.DestroyImmediate(owner.gameObject);

            Messages.Broadcast(new TestMessage());

            Assert.IsFalse(listener.IsListening);

            Assert.AreEqual(before - 1, Messages<TestMessage>.ListenerCount);

            Assert.AreEqual(1, calls.Calls, "The other listeners still get it.");
        }

        [Test]
        public void AListenerAddedDuringABroadcast_WaitsForTheNextOne()
        {
            var late = new Counter();

            var holder = new List<MessageListener>();

            Keep(Messages<TestMessage>.Listen(holder, static (list, _) =>
            {
                if (list.Count == 0)
                {
                    list.Add(Messages<TestMessage>.Listen(new Counter(), static (c, _) => c.Calls++));
                }
            }));

            Messages.Broadcast(new TestMessage());

            Keep(holder[0]);

            Assert.AreEqual(2, Messages<TestMessage>.ListenerCount);

            Keep(Messages<TestMessage>.Listen(late, static (c, _) => c.Calls++));

            Messages.Broadcast(new TestMessage());

            Assert.AreEqual(1, late.Calls);
        }

        [Test]
        public void AListenerRemovedDuringABroadcast_IsNotCalled()
        {
            var second = new Counter();

            var secondListener = new MessageListener[1];

            // A message type no other test uses: its slots are fresh, so the remover is visited first.
            Keep(Messages<RemovalMessage>.Listen(secondListener, static (holder, _) => holder[0].Dispose()));

            secondListener[0] = Messages<RemovalMessage>.Listen(second, static (c, _) => c.Calls++);

            Messages.Broadcast(new RemovalMessage());

            Assert.AreEqual(0, second.Calls);
        }

        [Test]
        public void AThrowingListener_DoesNotStopTheOthers()
        {
            var counter = new Counter();

            Keep(Messages<TestMessage>.Listen(static _ => throw new InvalidOperationException("listener failed")));

            Keep(Messages<TestMessage>.Listen(counter, static (c, _) => c.Calls++));

            LogAssert.Expect(LogType.Exception, new Regex("listener failed"));

            Messages.Broadcast(new TestMessage());

            Assert.AreEqual(1, counter.Calls);
        }
    }
}

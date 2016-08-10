using NUnit.Framework;
using System;
using System.Threading;

namespace SharpRUDP.Test
{
    public class KeepAliveTest : NUnitTestClass
    {
        private bool _testServer = false;

        public KeepAliveTest(bool testServer)
        {
            _testServer = testServer;
        }

        public override void Run()
        {
            RUDPConnection s = new RUDPConnection();
            RUDPConnection c = new RUDPConnection();

            s.Create(true, "127.0.0.1", 80);
            c.Create(false, "127.0.0.1", 80);
            c.RequestChannel("TEST");

            c.OnChannelAssigned += (RUDPChannel ch) =>
            {
                ch.Connect();
            };

            c.OnConnected += (RUDPChannel ch) =>
            {
                Console.WriteLine("10 seconds for keepalive START...");
                Thread.Sleep(5000);
                if (_testServer)
                    c.Disconnect();
                else
                    s.Disconnect();
                Thread.Sleep(5000);
                Console.WriteLine("10 seconds for keepalive END!");
                Thread.Sleep(2500);

                s.Disconnect();
                c.Disconnect();
            };

            while (c.State < State.CLOSING || s.State < State.CLOSING)
                Thread.Sleep(10);

            Assert.AreEqual(State.CLOSED, s.State);
            Assert.AreEqual(State.CLOSED, c.State);
        }
    }
}

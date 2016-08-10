using NUnit.Framework;
using System.Threading;

namespace SharpRUDP.Test
{
    public class ConnectionTest : NUnitTestClass
    {
        public override void Run()
        {
            bool finished = false;

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
                finished = true;
            };

            while (!finished)
                Thread.Sleep(10);

            s.Disconnect();
            c.Disconnect();

            Assert.AreEqual(State.CLOSED, s.State);
            Assert.AreEqual(State.CLOSED, c.State);
        }
    }
}

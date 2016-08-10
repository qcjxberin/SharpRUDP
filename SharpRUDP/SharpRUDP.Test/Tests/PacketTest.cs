using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;

namespace SharpRUDP.Test
{
    public class PacketTest : NUnitTestClass
    {
        int _packetMax;
        int _packetSize;
        int _multiplier;

        public PacketTest(int max, int size, int multiplier = 1024)
        {
            _packetMax = max;
            _packetSize = size;
            _multiplier = multiplier;
        }

        public override void Run()
        {
            bool finished = false;

            RUDPConnection s = new RUDPConnection();
            RUDPConnection c = new RUDPConnection();

            s.Create(true, "127.0.0.1", 80);
            c.Create(false, "127.0.0.1", 80);
            c.RequestChannel("TEST");

            byte[] buf = new byte[_packetSize * _multiplier];
            Random r = new Random(DateTime.Now.Second);
            r.NextBytes(buf);
            int counter = 0;

            c.OnChannelAssigned += (RUDPChannel ch) =>
            {
                ch.Connect();
            };

            c.OnConnected += (RUDPChannel ch) =>
            {
                counter = 0;
                finished = false;
                for (int i = 0; i < _packetMax; i++)
                    ch.SendData(buf);
            };

            s.OnPacketReceived += (RUDPChannel ch, RUDPPacket p) =>
            {
                Assert.IsTrue(p.Data.SequenceEqual(buf));
                counter++;
                if (counter >= _packetMax)
                    finished = true;
            };

            while (!finished)
                Thread.Sleep(10);

            c.Disconnect();
            s.Disconnect();

            Assert.AreEqual(State.CLOSED, s.State);
            Assert.AreEqual(State.CLOSED, c.State);
        }
    }
}

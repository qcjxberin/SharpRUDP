﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Text;
using System.Threading;

namespace SharpRUDP.Test
{
    [TestClass]
    public class MediumPacketTest
    {
        [TestMethod, Timeout(30000)]
        public void MediumPacket()
        {
            bool finished = false;

            RUDPConnection s = new RUDPConnection();
            RUDPConnection c = new RUDPConnection();
            s.Listen("127.0.0.1", 80);
            c.Connect("127.0.0.1", 80);
            while (c.State != ConnectionState.OPEN)
                Thread.Sleep(10);
            Assert.AreEqual(ConnectionState.OPEN, c.State);

            int counter = 0;
            s.OnPacketReceived += (RUDPPacket p) =>
            {
                Assert.AreEqual("SEQUENCEDDATAMEDIUMPACKETLENGTH" + counter, Encoding.ASCII.GetString(p.Data));
                counter++;
                if (counter >= 500)
                    finished = true;
            };

            Random r = new Random(DateTime.Now.Second);
            for (int i = 0; i < 500; i++)
            {
                Thread.Sleep(3 * r.Next(0, 10));
                c.Send("SEQUENCEDDATAMEDIUMPACKETLENGTH" + i.ToString());
            }

            while (!finished)
                Thread.Sleep(10);

            counter = 0;
            finished = false;
            for (int i = 0; i < 500; i++)
                c.Send("SEQUENCEDDATAMEDIUMPACKETLENGTH" + i.ToString());

            while (!finished)
                Thread.Sleep(10);

            s.Disconnect();
            c.Disconnect();
            while (c.State != ConnectionState.CLOSED && s.State != ConnectionState.CLOSED)
                Thread.Sleep(10);
            Assert.AreEqual(ConnectionState.CLOSED, s.State);
            Assert.AreEqual(ConnectionState.CLOSED, c.State);
        }
    }
}

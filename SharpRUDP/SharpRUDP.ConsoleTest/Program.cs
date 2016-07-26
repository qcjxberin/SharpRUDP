using System;
using System.Threading;

namespace SharpRUDP.ConsoleTest
{
    class Program
    {
        static void Main(string[] args)
        {

            RUDPConnection s = new RUDPConnection();
            s.Create(true, "127.0.0.1", 80);

            Thread.Sleep(1000);

            RUDPConnection c = new RUDPConnection();
            c.Create(false, "127.0.0.1", 80);
            c.OnChannelAssigned += (RUDPChannel channel) =>
            {
                channel.Connect();
            };
            c.RequestChannel(c.RemoteAddress, "TEST");

            Console.ReadKey();

        }
    }
}

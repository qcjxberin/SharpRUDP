using SharpRUDP.Test;
using System;
using System.Diagnostics;
using System.Threading;

namespace SharpRUDP.LiveTest
{
    class Program
    {
        static void Wait()
        {
            // Console.ReadLine();
            //Thread.Sleep(5000);
        }

        static void RunAllTests()
        {
            foreach (NUnitTestClass test in ControllingTestOrder.CLITestSource)
            {
                Stopwatch sw = Stopwatch.StartNew();
                sw.Start();
                Console.WriteLine("=================================== TEST START: {0}", test.TestName);
                test.Run();
                sw.Stop();
                Console.WriteLine("=================================== TEST FINISH: {0} - {1}", test.TestName, sw.Elapsed);
            }
        }

        static void Main(string[] args)
        {
            //RunAllTests();
            //new ClientDisconnectionTest().Run(); Wait();
            //new ConnectionTest().Run();
            //new ServerDisconnectionTest().Run(); Wait();
            //new PacketTest(100, 1).Run(); Wait();
            //new KeepAliveTest(true).Run(); Wait();
            //new KeepAliveTest(false).Run(); Wait();
            //new PacketTest(100, 8, 1) { TestName = "8 bytes" }.Run();
            new PacketTest(100, 64) { TestName = "64 Kbytes" }.Run();

            Console.WriteLine("Test finished");
            //new PacketTest(100, 32) { TestName = "32 Kbytes" }.Run();
            //Wait();
            Console.WriteLine("Finished");
            Console.ReadKey();
        }
    }
}

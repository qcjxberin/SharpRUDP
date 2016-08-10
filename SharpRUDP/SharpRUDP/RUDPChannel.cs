using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

namespace SharpRUDP
{
    public class RUDPChannel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsServer { get; set; }
        public bool IsUsed { get; set; }
        public int Local { get; set; }
        public int Remote { get; set; }
        public int PacketId { get; set; }
        public int MTU { get; set; }
        public int ServerStartSequence { get; set; }
        public int ClientStartSequence { get; set; }
        public IPEndPoint EndPoint { get; set; }
        public RUDPConnection Connection { get; set; }
        public State State { get; set; }

        private List<int> _ackSequences { get; set; }
        private List<RUDPPacket> _pending { get; set; }
        private List<RUDPPacket> _sendQueue { get; set; }
        private List<RUDPPacket> _recvQueue { get; set; }
        private List<int> _processedSequences { get; set; }
        private int _maxMTU { get { return (int)(MTU * 0.80); } }

        internal DateTime LastKeepAliveReceived { get; set; }

        private object _ackMutex = new object();
        private object _seqMutex = new object();
        private object _sendMutex = new object();
        private object _recvMutex = new object();
        private Thread _thSend;
        private Thread _thRecv;
        private Thread _thKeepAlive;
        private Thread _thRetransmit;
        private AutoResetEvent _evRecv;
        private AutoResetEvent _evSend;
        private bool _hasPending;

        public RUDPChannel()
        {
            State = State.OPENING;
            _evRecv = new AutoResetEvent(false);
            _evSend = new AutoResetEvent(false);
            LastKeepAliveReceived = DateTime.MinValue;
        }

        public RUDPChannel Init()
        {
            State = State.OPENING;

            PacketId = 1;
            MTU = 1024 * 8;
            ClientStartSequence = 100;
            ServerStartSequence = 200;
            Local = IsServer ? ServerStartSequence : ClientStartSequence;
            Remote = IsServer ? ClientStartSequence : ServerStartSequence;

            _ackSequences = new List<int>();
            _pending = new List<RUDPPacket>();
            _recvQueue = new List<RUDPPacket>();
            _sendQueue = new List<RUDPPacket>();
            _processedSequences = new List<int>();
            _thRecv = new Thread(new ThreadStart(RecvThread));
            _thSend = new Thread(new ThreadStart(SendThread));
            _thKeepAlive = new Thread(new ThreadStart(KeepAliveThread));
            _thRetransmit = new Thread(new ThreadStart(RetransmitThread));
            _thRecv.Start();
            _thSend.Start();
            _thKeepAlive.Start();
            _thRetransmit.Start();

            State = State.OPEN;

            return this;
        }

        public void AddReceivedPacket(RUDPPacket p)
        {
            if(!IsUsed)
                IsUsed = true;
            lock (_recvMutex)
                _recvQueue.Add(p);
            _evRecv.Set();
        }

        public void AcknowledgePacket(int sequence)
        {
            lock (_ackMutex)
                _ackSequences.Add(sequence);
        }

        public void Connect()
        {
            State = State.CONNECTING;
            SendPacket(RUDPPacketType.SYN);
        }

        public void SendData(byte[] data)
        {
            if ((!IsServer && State != State.CONNECTED) || (IsServer && State != State.OPEN))
                throw new Exception("Cannot send data to an unconnected channel. If it's a client, try to Connect() first.");
            SendPacket(RUDPPacketType.DAT, data);
        }

        private void SendPacket(RUDPPacketType type, byte[] data = null)
        {
            lock (_sendMutex)
                _sendQueue.AddRange(PreparePackets(type, data));
            _evSend.Set();
        }

        private void SendThread()
        {
            DateTime dtNow;
            while (State < State.CLOSING)
            {
                dtNow = DateTime.Now;
                lock (_sendMutex)
                {
                    if (_hasPending)
                    {
                        foreach (RUDPPacket p in _pending.Where(x => (dtNow - x.Sent).Seconds > 1))
                        {
                            RUDPConnection.Debug("RETRANSMIT -> {0}: {1}", EndPoint, p);
                            p.Sent = dtNow;
                            p.SentTicks = Environment.TickCount;
                            Connection._socket.SendBytes(EndPoint, Connection.Serializer.Serialize(Connection.PacketHeader, p));
                        }
                        lock (_ackSequences)
                        {
                            _pending.RemoveAll(x => _ackSequences.Contains(x.Seq));
                            _ackSequences.Clear();
                        }
                    }
                    else
                    {
                        foreach (RUDPPacket p in _sendQueue)
                        {
                            RUDPConnection.Debug("SEND -> {0}: {1}", EndPoint, p);
                            p.Sent = dtNow;
                            p.SentTicks = Environment.TickCount;
                            _pending.Add(p);
                            Connection._socket.SendBytes(EndPoint, Connection.Serializer.Serialize(Connection.PacketHeader, p));
                        }
                        _sendQueue = new List<RUDPPacket>();
                    }
                }
                _evSend.WaitOne(1000);
            }
            RUDPConnection.Trace("Bye SendThread");
        }

        private List<RUDPPacket> PreparePackets(RUDPPacketType type, byte[] data)
        {
            List<RUDPPacket> rv = new List<RUDPPacket>();
            if ((data != null && data.Length <= _maxMTU) || data == null)
            {
                lock (_seqMutex)
                {
                    rv.Add(new RUDPPacket()
                    {
                        Serializer = Connection.Serializer,
                        Channel = Id,
                        Qty = 0,
                        Data = data,
                        Type = type,
                        Seq = Local,
                        Id = PacketId
                    });
                    Local++;
                    PacketId++;
                }
            }
            else if (data != null && data.Length >= _maxMTU)
            {
                int i = 0;
                while (i < data.Length)
                {
                    int min = i;
                    int max = _maxMTU;
                    if ((min + max) > data.Length)
                        max = data.Length - min;
                    byte[] buf = new byte[max];
                    Buffer.BlockCopy(data, i, buf, 0, max);
                    rv.Add(new RUDPPacket()
                    {
                        Serializer = Connection.Serializer,
                        Channel = Id,
                        Id = PacketId,
                        Seq = Local,
                        Type = type,
                        Data = buf
                    });
                    i += _maxMTU;
                    Local++;
                }
                foreach (RUDPPacket p in rv)
                    p.Qty = rv.Count;
                PacketId++;
            }

            foreach (RUDPPacket p in rv)
                RUDPConnection.Trace("Prepared packet {0}", p);

            return rv;
        }

        private void RecvThread()
        {
            while (State < State.CLOSING)
            {
                ProcessRecvQueue();
                _evRecv.WaitOne(1000);
            }
            RUDPConnection.Trace("Bye RecvThread");
        }

        private void ProcessRecvQueue()
        {
            List<RUDPPacket> RecvPackets;
            lock (_recvMutex)
                RecvPackets = _recvQueue.OrderBy(x => x.Seq).ToList();
            foreach (RUDPPacket p in RecvPackets)
            {
                if (p.Processed || _processedSequences.Contains(p.Seq))
                    continue;

                if (p.Seq != Remote)
                {
                    RUDPConnection.Trace("{0} != {1} | {2}", p.Seq, Remote, Name);

                    lock (_recvMutex)
                        _recvQueue.Add(p);
                    if (p.Seq > Remote)
                        continue;
                    else
                        break;
                }

                if (p.Qty == 0)
                {
                    Remote++;
                    p.Processed = true;
                    _processedSequences.Add(p.Seq);
                    RUDPConnection.Debug("RECV <- {0}: {1}", p.Src, p);

                    if (IsServer)
                    {
                        if (p.Type == RUDPPacketType.SYN)
                        {
                            SendPacket(RUDPPacketType.ACK);
                            LastKeepAliveReceived = DateTime.Now;
                            Connection.InvokeIncomingConnection(this);
                            continue;
                        }
                    }
                    else if (p.Type == RUDPPacketType.ACK)
                    {
                        State = State.CONNECTED;
                        LastKeepAliveReceived = DateTime.Now;
                        Connection.InvokeConnected(this);
                        continue;
                    }

                    if (p.Type == RUDPPacketType.DAT)
                        Connection.InvokePacketReceived(this, p);
                }
                else if (p.Qty > 0 && p.Type == RUDPPacketType.DAT)
                {
                    List<RUDPPacket> multiPackets = RecvPackets.Where(x => x.Id == p.Id).ToList();
                    multiPackets = multiPackets.GroupBy(x => x.Seq).Select(g => g.First()).OrderBy(x => x.Seq).ToList();
                    if (multiPackets.Count == p.Qty)
                    {
                        RUDPConnection.Debug("MULTIPACKET {0}", p.Id);

                        byte[] buf;
                        MemoryStream ms = new MemoryStream();
                        using (BinaryWriter bw = new BinaryWriter(ms))
                            foreach (RUDPPacket mp in multiPackets)
                            {
                                mp.Processed = true;
                                bw.Write(mp.Data);
                                RUDPConnection.Debug("RECV MP <- {0}: {1}", mp.Src, mp);
                            }
                        _processedSequences.AddRange(multiPackets.Select(x => x.Seq));
                        buf = ms.ToArray();
                        // Debug("MULTIPACKET ID {0} DATA: {1}", p.PacketId, Encoding.ASCII.GetString(buf));
                        Connection.InvokePacketReceived(this, new RUDPPacket()
                        {
                            Serializer = Connection.Serializer,
                            Data = buf,
                            Id = p.Id,
                            Qty = p.Qty,
                            Type = p.Type,
                            Seq = p.Seq
                        });

                        Remote += p.Qty;
                    }
                    else if (multiPackets.Count < p.Qty)
                    {
                        lock (_recvMutex)
                            _recvQueue.Add(p);
                        break;
                    }
                    else
                    {
                        RUDPConnection.Debug("P.QTY > MULTIPACKETS.COUNT ({0} > {1})", p.Qty, multiPackets.Count);
                        throw new Exception();
                    }
                }
            }
            lock (_recvMutex)
                _recvQueue.RemoveAll(x => x.Processed);
        }

        private void KeepAliveThread()
        {
            DateTime dtNow;
            while (State < State.CLOSING)
            {
                Thread.Sleep(1000);
                dtNow = DateTime.Now;
                if (LastKeepAliveReceived == DateTime.MinValue)
                    continue;
                if ((dtNow - LastKeepAliveReceived).Seconds > 2)
                    Connection._socket.SendBytes(EndPoint, new RUDPInternalPacket() { Type = RUDPInternalPacket.RUDPInternalPacketType.PING, Channel = Id, Data = 0 }.Serialize(Connection.PacketHeaderInternal));
                if ((dtNow - LastKeepAliveReceived).Seconds > 10)
                {
                    RUDPConnection.Trace("Remote does not answer");
                    Disconnect();
                }
            }
            RUDPConnection.Trace("Bye KeepAlive");
        }

        private void RetransmitThread()
        {
            DateTime dtNow;
            while (State < State.CLOSING)
            {
                dtNow = DateTime.Now;
                List<RUDPPacket> _tmp;
                lock (_sendMutex)
                    _tmp = new List<RUDPPacket>(_pending);
                int i = 0;
                int len = _tmp.Count - 1;
                int tc = Environment.TickCount;
                for (i = 0; i < len; i++)
                    if (Math.Abs(_tmp[i].SentTicks - tc) > 1000)
                        i = len + 10;
                _hasPending = i > len + 5;
                if (_hasPending)
                    _evSend.Set();
                Thread.Sleep(10);
            }
            RUDPConnection.Trace("Bye RetransmitThread");
        }

        public void Disconnect()
        {
            if (State >= State.OPENING && State < State.CLOSING)
                State = State.CLOSING;
            new Thread(() =>
            {
                Thread.Sleep(100);
                RUDPConnection.Trace("CLOSING channel {0}", Name);
                while (_thKeepAlive.IsAlive)
                    Thread.Sleep(1);
                while (_thRecv.IsAlive)
                    Thread.Sleep(1);
                while (_thRetransmit.IsAlive)
                    Thread.Sleep(1);
                while (_thSend.IsAlive)
                    Thread.Sleep(1);
                State = State.CLOSED;
                RUDPConnection.Trace("Channel {0} CLOSED", Name);
            }).Start();
        }

    }
}
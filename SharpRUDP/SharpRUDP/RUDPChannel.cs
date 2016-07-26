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
        public bool IsAlive { get; set; }
        public bool IsDropped { get; set; }
        public State State { get; set; }
        public int Local { get; set; }
        public int Remote { get; set; }
        public int PacketId { get; set; }
        public int MTU { get; set; }
        public int ServerStartSequence { get; set; }
        public int ClientStartSequence { get; set; }
        public IPEndPoint EndPoint { get; set; }
        public RUDPConnection Connection { get; set; }

        private List<RUDPPacket> _pending { get; set; }
        private List<RUDPPacket> _sendQueue { get; set; }
        private List<RUDPPacket> _recvQueue { get; set; }
        private List<int> _processedSequences { get; set; }
        private int _maxMTU { get { return (int)(MTU * 0.80); } }

        private object _seqMutex = new object();
        private object _sendMutex = new object();
        private object _recvMutex = new object();
        private Thread _thSend;
        private Thread _thRecv;

        public RUDPChannel()
        {
            State = State.OPENING;
        }

        public RUDPChannel Init()
        {
            IsAlive = true;
            IsUsed = false;
            IsDropped = false;

            PacketId = 1;
            MTU = 1024 * 8;
            ClientStartSequence = 100;
            ServerStartSequence = 200;
            Local = IsServer ? ServerStartSequence : ClientStartSequence;
            Remote = IsServer ? ClientStartSequence : ServerStartSequence;

            _pending = new List<RUDPPacket>();
            _recvQueue = new List<RUDPPacket>();
            _sendQueue = new List<RUDPPacket>();
            _processedSequences = new List<int>();
            _thRecv = new Thread(new ThreadStart(RecvThread));
            _thSend = new Thread(new ThreadStart(SendThread));
            _thRecv.Start();
            _thSend.Start();

            State = State.OPEN;

            return this;
        }

        public void AddReceivedPacket(RUDPPacket p)
        {
            IsUsed = true;
            lock (_recvMutex)
                _recvQueue.Add(p);
        }

        public void AcknowledgePacket(int sequence)
        {
            lock (_sendMutex)
                _pending.RemoveAll(x => x.Seq == sequence);
        }

        public void Connect()
        {
            State = State.CONNECTING;
            SendPacket(RUDPPacketType.SYN);
        }

        public void SendData(byte[] data)
        {
            SendPacket(RUDPPacketType.DAT, data);
        }

        private void SendPacket(RUDPPacketType type, byte[] data = null)
        {
            lock (_sendMutex)
                _sendQueue.AddRange(PreparePackets(type, data));
        }

        private void SendThread()
        {
            while (!IsDropped)
            {
                DateTime dtNow = DateTime.Now;
                lock (_sendMutex)
                {
                    if (_pending.Where(x => (dtNow - x.Sent).Seconds > 1).Count() > 0)
                        foreach (RUDPPacket p in _pending.Where(x => (dtNow - x.Sent).Seconds > 1))
                        {
                            RUDPConnection.Debug("RETRANSMIT -> {0}: {1}", EndPoint, p);
                            p.Sent = dtNow;
                            Connection._socket.SendBytes(EndPoint, Connection.Serializer.Serialize(Connection.PacketHeader, p));
                        }
                    else
                    {
                        foreach (RUDPPacket p in _sendQueue)
                        {
                            RUDPConnection.Debug("SEND -> {0}: {1}", EndPoint, p);
                            p.Sent = dtNow;
                            _pending.Add(p);
                            Connection._socket.SendBytes(EndPoint, Connection.Serializer.Serialize(Connection.PacketHeader, p));
                        }
                        _sendQueue = new List<RUDPPacket>();
                    }
                }
                Thread.Sleep(10);
            }
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
                    byte[] buf = data.Skip(i).Take(max).ToArray();
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
            while (!IsDropped)
            {
                ProcessRecvQueue();
                Thread.Sleep(10);
            }
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
                            Connection.InvokeIncomingConnection(this);
                            continue;
                        }
                    }
                    else if (p.Type == RUDPPacketType.ACK)
                    {
                        State = State.CONNECTED;
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
    }
}
using NLog;
using SharpRUDP.Serializers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SharpRUDP
{
    public class RUDPConnection
    {
        public bool IsServer { get; set; }
        public byte[] PacketHeader { get; set; }
        public byte[] PacketHeaderInternal { get; set; }
        public RUDPSerializer Serializer { get; set; }
        public IPEndPoint LocalEndpoint { get; set; }
        public IPEndPoint RemoteEndpoint { get; set; }
        public bool IsAlive { get; set; }

        private static Logger Log = LogManager.GetCurrentClassLogger();
        private object _chMutex = new object();
        private Dictionary<string, List<RUDPChannel>> _channels = new Dictionary<string, List<RUDPChannel>>();
        internal RUDPSocket _socket = new RUDPSocket();

        public delegate void dChannelEvent(RUDPChannel channel);
        public delegate void dPacketEvent(RUDPChannel channel, RUDPPacket p);
        public event dChannelEvent OnChannelAssigned;
        public event dChannelEvent OnConnected;
        public event dChannelEvent OnIncomingConnection;
        public event dPacketEvent OnPacketReceived;

        public static void Debug(string text, params object[] args) { Log.Debug(text, args); }
        public static void Trace(string text, params object[] args) { Log.Trace(text, args); }

        public RUDPConnection()
        {            
            Serializer = new RUDPBinarySerializer();
            // Serializer = new RUDPJSONSerializer();
            PacketHeader = new byte[] { 0xFF, 0x01 };
            PacketHeaderInternal = new byte[] { 0xFF, 0x02 };
            _channels = new Dictionary<string, List<RUDPChannel>>();
            _socket.OnDataReceived += ProcessChannelData;
        }

        public void Create(bool acceptIncomingConnections, string address, int port)
        {
            IsServer = acceptIncomingConnections;
            if (acceptIncomingConnections)
                LocalEndpoint = new IPEndPoint(IPAddress.Parse(address), port);
            else
            {
                TcpListener tcp = new TcpListener(IPAddress.Any, 0);
                tcp.Start();
                int localPort = ((IPEndPoint)tcp.LocalEndpoint).Port;
                tcp.Stop();
                LocalEndpoint = new IPEndPoint(IPAddress.Any, localPort);
                bool connect = false;
                IPAddress ipAddress;
                if (!IPAddress.TryParse(address, out ipAddress))
                {
                    foreach (IPAddress ip in Dns.GetHostEntry(address).AddressList.Where(x => !x.IsIPv6LinkLocal && !x.IsIPv6Multicast && !x.IsIPv6SiteLocal && !x.IsIPv6Teredo))
                    {
                        try
                        {
                            Debug("Trying {0}", ip);
                            RemoteEndpoint = new IPEndPoint(ip, port);
                            connect = true;
                            break;
                        }
                        catch (Exception) { }
                    }
                    if (!connect)
                        throw new Exception("Unable to connect");
                }
                else
                    RemoteEndpoint = new IPEndPoint(ipAddress, port);
                Debug("Using {0}", RemoteEndpoint);
            }            
            _socket.Bind(LocalEndpoint.Address.ToString(), LocalEndpoint.Port);
            IsAlive = true;
        }

        public void RequestChannel(IPEndPoint ep, string name)
        {
            _socket.SendBytes(ep, new RUDPInternalPacket() { Type = RUDPInternalPacket.RUDPInternalPacketType.CHANNELREQUEST, Channel = 0, ExtraData = name }.Serialize(PacketHeaderInternal));
        }

        private void CleanChannels()
        {
            List<KeyValuePair<string, int>> deadChannels = new List<KeyValuePair<string, int>>();
            foreach (string ep in _channels.Keys)
                foreach (RUDPChannel c in _channels[ep])
                    if (!c.IsAlive)
                        deadChannels.Add(new KeyValuePair<string, int>(ep, c.Id));
            foreach (var kvp in deadChannels)
                _channels[kvp.Key].RemoveAll(x => x.Id == kvp.Value);

        }

        private int RequestFreeChannel(IPEndPoint ep, string name)
        {
            lock (_chMutex)
            {
                string strEp = ep.ToString();
                if (!_channels.ContainsKey(strEp))
                    _channels[strEp] = new List<RUDPChannel>();
                foreach (RUDPChannel c in _channels[strEp])
                    if (c != null && !c.IsUsed && !c.IsDropped)
                        return c.Id;
                int id = _channels.Count();
                _channels[strEp].Add(new RUDPChannel()
                {
                    Connection = this,
                    Id = id,
                    Name = name,
                    EndPoint = ep,
                    IsServer = true
                }.Init());
                return id;
            }
        }

        private void ProcessChannelData(IPEndPoint ep, byte[] data, int length)
        {
            RUDPPacket p;
            RUDPChannel channel;
            RUDPInternalPacket ip;
            DateTime dtNow = DateTime.Now;

            Trace("RECV: {0}", Encoding.ASCII.GetString(data));

            string strEp = ep.ToString();
            bool isNormalPacket = length >= PacketHeader.Length && data.Take(PacketHeader.Length).SequenceEqual(PacketHeader);
            bool isInternalPacket = length >= PacketHeaderInternal.Length && data.Take(PacketHeaderInternal.Length).SequenceEqual(PacketHeaderInternal);

            if (isInternalPacket)
            {
                ip = RUDPInternalPacket.Deserialize(PacketHeaderInternal, data);
                Trace("INTERNAL RECV <- {0}: {1}", ep, ip);

                if (ip.Channel == 0)
                {
                    switch (ip.Type)
                    {
                        case RUDPInternalPacket.RUDPInternalPacketType.CHANNELREQUEST:
                            string channelName = ip.ExtraData;
                            int channelId = RequestFreeChannel(ep, channelName);
                            Trace("Assigning channel {0} as {1}", channelId, channelName);
                            ip = new RUDPInternalPacket() { Type = RUDPInternalPacket.RUDPInternalPacketType.CHANNELASSIGN, Channel = 0, Data = channelId, ExtraData = channelName };
                            _socket.SendBytes(ep, ip.Serialize(PacketHeaderInternal));
                            break;
                        case RUDPInternalPacket.RUDPInternalPacketType.CHANNELASSIGN:
                            if (!_channels.ContainsKey(strEp))
                                _channels[strEp] = new List<RUDPChannel>();
                            Trace("Channel {0} assigned as {1}", ip.ExtraData, ip.Data);
                            RUDPChannel c = new RUDPChannel()
                            {
                                Connection = this,
                                Id = ip.Data,
                                Name = ip.ExtraData,
                                IsServer = false,
                                EndPoint = ep
                            }.Init();
                            _channels[strEp].Add(c);
                            OnChannelAssigned?.Invoke(c);
                            break;
                    }
                }
                else
                {
                    channel = _channels[strEp].Where(x => x.Id == ip.Channel).SingleOrDefault();
                    if (channel == null || channel.IsDropped)
                    {
                        Trace("Drop connection {0}", ip.Channel);
                        return;
                    }

                    switch (ip.Type)
                    {
                        case RUDPInternalPacket.RUDPInternalPacketType.ACK:
                            channel.AcknowledgePacket(ip.Data);
                            break;
                    }
                }
            }
            else if (isNormalPacket)
            {
                p = Serializer.Deserialize(PacketHeader, data);
                channel = _channels[strEp].Where(x => x.Id == p.Channel).SingleOrDefault();
                if (channel != null)
                {
                    p.Src = ep;
                    p.Serializer = Serializer;
                    Trace("ADDRECV: {0}", p);
                    channel.AddReceivedPacket(p);
                    ip = new RUDPInternalPacket() { Type = RUDPInternalPacket.RUDPInternalPacketType.ACK, Channel = p.Channel, Data = p.Seq };
                    Trace("INTERNAL SEND -> {0}: {1}", ep, ip);
                    _socket.SendBytes(ep, ip.Serialize(PacketHeaderInternal));
                }
                else
                    Trace("Unknown channel {0} for {1}", p.Channel, p);
            }
        }

        internal void InvokeConnected(RUDPChannel channel)
        {
            OnConnected?.Invoke(channel);
        }

        internal void InvokePacketReceived(RUDPChannel channel, RUDPPacket p)
        {
            OnPacketReceived?.Invoke(channel, p);
        }

        internal void InvokeIncomingConnection(RUDPChannel channel)
        {
            OnIncomingConnection?.Invoke(channel);
        }
    }
}

using NLog;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SharpRUDP
{
    public class RUDPSocket : IDisposable
    {
        public IPEndPoint LocalEndPoint;
        public IPEndPoint RemoteEndPoint;

        public delegate void dlgDataReceived(IPEndPoint ep, byte[] data, int length);
        public delegate void dlgSocketError(IPEndPoint ep, Exception ex);
        public event dlgDataReceived OnDataReceived;
        public event dlgSocketError OnSocketError;

        internal Socket _socket;
        internal bool _debugEnabled = false;

        private int _port;
        private string _address;
        private const int bufSize = 64 * 1024;
        private StateObject state = new StateObject();
        private EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        private AsyncCallback recv = null;
        private static Logger Log = LogManager.GetCurrentClassLogger();
        private bool _isDisposed;

        public class StateObject
        {
            public byte[] buffer = new byte[bufSize];
        }

        public void Bind(string address, int port)
        {
            _port = port;
            _address = address;
            IPAddress ip;
            if (address.Trim() != "0.0.0.0")
                ip = IPAddress.Parse(address);
            else
                ip = IPAddress.Any;
            LocalEndPoint = new IPEndPoint(ip, port);
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
            _socket.Bind(LocalEndPoint);
            Receive();
        }

        public void SendBytes(IPEndPoint endPoint, byte[] data)
        {
            PacketSending(endPoint, data, data.Length);
            try
            {
                _socket.BeginSendTo(data, 0, data.Length, SocketFlags.None, endPoint, (ar) =>
                {
                    try
                    {
                        StateObject so = (StateObject)ar.AsyncState;
                        int bytes = _socket.EndSend(ar);
                    }
                    catch (Exception ex)
                    {
                        OnSocketError?.Invoke(endPoint, ex);
                    }
                }, state);
            }
            catch (Exception ex)
            {
                OnSocketError?.Invoke(endPoint, ex);
            }
        }

        private void Receive()
        {
            _socket.BeginReceiveFrom(state.buffer, 0, bufSize, SocketFlags.None, ref ep, recv = (ar) =>
            {
                StateObject so = (StateObject)ar.AsyncState;
                try
                {
                    int bytes = _socket.EndReceiveFrom(ar, ref ep);
                    byte[] data = new byte[bytes];
                    Buffer.BlockCopy(so.buffer, 0, data, 0, bytes);
                    _socket.BeginReceiveFrom(so.buffer, 0, bufSize, SocketFlags.None, ref ep, recv, so);
                    PacketReceive((IPEndPoint)ep, data, bytes);
                }
                catch (Exception ex)
                {
                    OnSocketError?.Invoke((IPEndPoint)ep, ex);
                }
            }, state);
        }

        public virtual int PacketSending(IPEndPoint endPoint, byte[] data, int length)
        {
            if(_debugEnabled)
                Log.Trace("SEND -> {0}: {1}", endPoint, Encoding.ASCII.GetString(data, 0, length));
            return -1;
        }

        public void PacketReceive(IPEndPoint ep, byte[] data, int length)
        {
            if (_debugEnabled)
                Log.Trace("RECV <- {0}: {1}", ep, Encoding.ASCII.GetString(data, 0, length));
            OnDataReceived?.Invoke(ep, data, length);
        }

        protected virtual void Dispose(bool value)
        {
            _socket.Dispose();
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _socket.Shutdown(SocketShutdown.Both);
                Dispose(true);
                _isDisposed = true;
            }
            GC.SuppressFinalize(this);
        }

        internal void Disconnect()
        {
            Dispose();
        }
    }
}

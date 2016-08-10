using SharpRUDP.Serializers;
using System;
using System.Net;
using System.Web.Script.Serialization;

namespace SharpRUDP
{
    public class RUDPPacket
    {
        [ScriptIgnore]
        public RUDPSerializer Serializer { get; set; }
        [ScriptIgnore]
        public IPEndPoint Src { get; set; }
        [ScriptIgnore]
        public bool Processed { get; set; }
        [ScriptIgnore]
        public DateTime Sent { get; set; }
        [ScriptIgnore]
        public int SentTicks { get; set; }

        public int Seq { get; set; }
        public int Channel { get; set; }
        public int Id { get; set; }
        public int Qty { get; set; }
        public RUDPPacketType Type { get; set; }
        public byte[] Data { get; set; }

        public override string ToString()
        {
            return Serializer.AsString(this);
        }
    }
}

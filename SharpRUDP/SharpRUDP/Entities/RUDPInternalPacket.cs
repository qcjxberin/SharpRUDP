using System.IO;

namespace SharpRUDP
{
    public class RUDPInternalPacket
    {
        public enum RUDPInternalPacketType
        {
            ACK = 0x01,
            PING = 0x02,
            CHANNELREQUEST = 0x03,
            CHANNELASSIGN = 0x04
        }

        public RUDPInternalPacketType Type { get; set; }
        public int Channel { get; set; }
        public int Data { get; set; }
        public string ExtraData { get; set; }

        public byte[] Serialize(byte[] header)
        {
            MemoryStream ms = new MemoryStream();
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(header);
                bw.Write(Channel);
                bw.Write((byte)Type);
                bw.Write(Data);
                bw.Write(string.IsNullOrEmpty(ExtraData) ? "" : ExtraData);
            }
            return ms.ToArray();
        }

        public static RUDPInternalPacket Deserialize(byte[] header, byte[] data)
        {
            RUDPInternalPacket p = new RUDPInternalPacket();
            MemoryStream ms = new MemoryStream(data);
            using (BinaryReader br = new BinaryReader(ms))
            {
                br.ReadBytes(header.Length);
                p.Channel = br.ReadInt32();
                p.Type = (RUDPInternalPacketType)br.ReadByte();
                p.Data = br.ReadInt32();
                p.ExtraData = br.ReadString();
            }
            return p;
        }

        public override string ToString()
        {
            return string.Format("({0}: {1} | {2})", Type.ToString(), Data, ExtraData);
        }
    }
}

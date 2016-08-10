using System.IO;
using System.Text;

namespace SharpRUDP.Serializers
{
    public class RUDPBinarySerializer : RUDPSerializer
    {
        public override RUDPPacket Deserialize(byte[] header, byte[] data)
        {
            RUDPPacket p = new RUDPPacket();
            MemoryStream ms = new MemoryStream(data);
            using (BinaryReader br = new BinaryReader(ms))
            {
                br.ReadBytes(header.Length);
                p.Channel = br.ReadInt32();
                p.Seq = br.ReadInt32();
                p.Id = br.ReadInt32();
                p.Qty = br.ReadInt32();
                p.Type = (RUDPPacketType)br.ReadByte();
                int dataLen = br.ReadInt32();
                p.Data = br.ReadBytes(dataLen);
            }
            return p;
        }

        public override byte[] Serialize(byte[] header, RUDPPacket p)
        {
            MemoryStream ms = new MemoryStream();
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(header);
                bw.Write(p.Channel);
                bw.Write(p.Seq);
                bw.Write(p.Id);
                bw.Write(p.Qty);
                bw.Write((byte)p.Type);
                bw.Write(p.Data == null ? 0 : p.Data.Length);
                if (p.Data != null)
                    bw.Write(p.Data);
            }
            return ms.ToArray();
        }

        public override string AsString(RUDPPacket p)
        {
            return string.Format("CH:{0} | SEQ:{1} | ID:{2} | TYPE:{3} | QTY:{4} | DATA:{5}",
                p.Channel,
                p.Seq,
                p.Id,
                p.Type.ToString(),
                p.Qty,
                p.Data == null ? "" : (p.Data.Length > 64 ? p.Data.Length.ToString() : string.Join(",", Encoding.ASCII.GetString(p.Data)))
            );
        }
    }
}

using OpenKh.Kh2;
using OpenKh.Ps2;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenKh.Engine.Parsers.Kddf2
{
    public class ImmutableMesh
    {
        public Mdlx.DmaChain DmaChain { get; }
        public List<VpuPacket> VpuPackets { get; }

        public int TextureIndex => DmaChain.TextureIndex;
        public bool IsOpaque => (DmaChain.RenderFlags & 1) == 0;

        public ImmutableMesh(Mdlx.DmaChain dmaChain)
        {
            DmaChain = dmaChain;
            VpuPackets = dmaChain.DmaVifs
                .Select(dmaVif =>
                {
                    var unpacker = new VifUnpacker(dmaVif.VifPacket);
                    unpacker.Run();

                    using (var stream = new MemoryStream(unpacker.Memory))
                        return VpuPacket.Read(stream);
                })
                .ToList();
        }
    }
}

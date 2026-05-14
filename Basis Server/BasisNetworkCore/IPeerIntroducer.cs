using System.Net;

namespace Basis.Network.Core
{
    public interface IPeerIntroducer
    {
        bool Initialize(NetManager activeManager);
        void Introduce(IPEndPoint aInternal, IPEndPoint aExternal,
                       IPEndPoint bInternal, IPEndPoint bExternal,
                       string token);
        bool IsPairOffloaded(int peerIdA, int peerIdB);
        void Shutdown();
    }
}

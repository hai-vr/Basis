using System.Collections.Concurrent;
namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public class QueuedMessagePool
    {
        private static readonly ConcurrentQueue<QueuedMessage> pool = new();

        public static QueuedMessage Rent()
        {
            return pool.TryDequeue(out var msg) ? msg : new QueuedMessage();
        }

        public static void Return(QueuedMessage msg)
        {
            msg.FromPeer = null;
            msg.AvatarMessage = default;
            pool.Enqueue(msg);
        }
    }
}

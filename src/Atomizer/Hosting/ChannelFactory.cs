using System.Threading.Channels;

namespace Atomizer.Hosting
{
    internal static class ChannelFactory
    {
        public static Channel<T> CreateBounded<T>(int capacity)
        {
            return Channel.CreateBounded<T>(
                new BoundedChannelOptions(capacity)
                {
                    SingleReader = false,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait,
                }
            );
        }
    }
}

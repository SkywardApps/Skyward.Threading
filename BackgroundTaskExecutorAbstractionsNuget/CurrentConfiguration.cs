using System.Collections.Generic;
using System.Threading;

namespace Skyward.Threading.Abstractions
{
    public class CurrentConfiguration
    {
        public int ConcurrentGeneralBackgroundThreads { get; }
        public IEnumerable<BackgroundQueueConfig> Queues { get; }

        public CurrentConfiguration(int concurrentGeneralBackgroundThreads, IEnumerable<BackgroundQueueConfig> queues)
        {
            ConcurrentGeneralBackgroundThreads = concurrentGeneralBackgroundThreads;
            Queues = queues;
        }
    }
}

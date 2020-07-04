namespace Skyward.Threading
{
    public class BackgroundQueueConfig
    {
        public string Name { get; set; }
        public int MaximumConcurrentExecutions { get; set; }
    }
}

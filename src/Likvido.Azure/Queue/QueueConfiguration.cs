namespace Likvido.Azure.Queue
{
    public class QueueConfiguration
    {
        public string ConnectionString { get; set; }

        /// <summary>
        /// This should be a unique identifier for the application that publishes messages to the queue.
        /// It should be in a URI format, e.g. /accounting/api
        /// </summary>
        public string DefaultSource { get; set; }
    }
}

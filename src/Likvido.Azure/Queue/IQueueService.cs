using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Likvido.CloudEvents;

namespace Likvido.Azure.Queue
{
    public interface IQueueService
    {
        [Obsolete("Please switch to sending messages in the CloudEvent format by using one of the other overloads")]
        Task SendAsync(
            string queueName,
            object message,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default);

        [Obsolete("Please switch to sending messages in the CloudEvent format by using one of the other overloads")]
        Task SendAsync(string queueName,
            IEnumerable<object> messages,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default);

        Task SendAsync(
            string queueName,
            IEnumerable<CloudEvent> cloudEvents,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default);

        Task SendAsync(
            string queueName,
            CloudEvent cloudEvent,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default);

        Task SendAsync<T>(
            string queueName,
            string type,
            T data,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default);
    }
}

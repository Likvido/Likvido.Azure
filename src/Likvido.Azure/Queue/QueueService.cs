using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Queues;
using Likvido.CloudEvents;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;

namespace Likvido.Azure.Queue
{
    public class QueueService : IQueueService
    {
        private readonly QueueServiceClient _queueServiceClient;
        public QueueService(QueueServiceClient queueServiceClient)
        {
            _queueServiceClient = queueServiceClient;
        }

        public async Task SendAsync(
            string queueName,
            IEnumerable<CloudEvent> cloudEvents,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            foreach (var cloudEvent in cloudEvents)
            {
                await SendAsync(queueName, cloudEvent, initialVisibilityDelay, timeToLive, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SendAsync<T>(
            string queueName,
            string source,
            string type,
            T data,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            var cloudEvent = new CloudEvent<T>
            {
                Source = source,
                Type = type,
                Data = data,
                Time = DateTime.UtcNow
            };

            await SendAsync(queueName, cloudEvent, initialVisibilityDelay, timeToLive, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendAsync(
            string queueName,
            CloudEvent cloudEvent,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            if (!cloudEvent.Time.HasValue)
            {
                cloudEvent.Time = DateTime.UtcNow;
            }

            await SendMessageAsync(queueName, cloudEvent, initialVisibilityDelay, timeToLive, cancellationToken).ConfigureAwait(false);
        }

        [Obsolete("Please switch to sending messages in the CloudEvent format by using one of the other overloads")]
        public async Task SendAsync(
            string queueName,
            object message,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            await SendMessageAsync(queueName, message, initialVisibilityDelay, timeToLive, cancellationToken).ConfigureAwait(false);
        }

        [Obsolete("Please switch to sending messages in the CloudEvent format by using one of the other overloads")]
        public async Task SendAsync(
            string queueName,
            IEnumerable<object> messages,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            foreach (var message in messages)
            {
                await SendMessageAsync(queueName, message, initialVisibilityDelay, timeToLive, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task SendMessageAsync(
            string queueName,
            object message,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            await new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                    Delay = TimeSpan.FromSeconds(5),
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential
                })
                .Build()
                .ExecuteAsync(async _ =>
                    {
                        var queue = _queueServiceClient.GetQueueClient(queueName);
                        try
                        {
                            await queue.SendMessageAsync(
                                    JsonConvert.SerializeObject(message),
                                    timeToLive: timeToLive ?? TimeSpan.FromSeconds(-1), // Using -1 means that the message does not expire.
                                    visibilityTimeout: initialVisibilityDelay,
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
                        {
                            await queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                        }
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

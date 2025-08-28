using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Queues;
using Likvido.Azure.Extensions;
using Likvido.CloudEvents;
using Likvido.Identity.PrincipalProviders;
using System.Text.Json;
using Polly;
using Polly.Retry;
using Microsoft.Extensions.Logging;

namespace Likvido.Azure.Queue
{
    public class QueueService : IQueueService
    {
        private readonly QueueServiceClient _queueServiceClient;
        private readonly IPrincipalProvider _principalProvider;
        private readonly string _defaultSource;
        private readonly ILogger<QueueService> _logger;

        public QueueService(QueueServiceClient queueServiceClient, IPrincipalProvider principalProvider, string defaultSource, ILogger<QueueService> logger)
        {
            _queueServiceClient = queueServiceClient;
            _principalProvider = principalProvider;
            _defaultSource = defaultSource;
            _logger = logger;
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
            string type,
            T data,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            var cloudEvent = new CloudEvent<T>
            {
                Source = _defaultSource,
                Type = type,
                Data = data
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

            if (string.IsNullOrWhiteSpace(cloudEvent.Source))
            {
                cloudEvent.Source = _defaultSource;
            }

            if (_principalProvider.User != null)
            {
                cloudEvent.LikvidoUserClaimsString = _principalProvider.User.GetAllClaimsAsJsonString();
            }

            await SendMessageAsync(queueName, cloudEvent, initialVisibilityDelay, timeToLive, cancellationToken).ConfigureAwait(false);
        }

        private async Task SendMessageAsync(
            string queueName,
            object message,
            TimeSpan? initialVisibilityDelay = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(queueName))
            {
                throw new ArgumentException("Queue name cannot be null or empty, please check the configuration.", nameof(queueName));
            }

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
                        var serializedMessage = JsonSerializer.Serialize(message);
                        try
                        {
                            await queue.SendMessageAsync(
                                    serializedMessage,
                                    timeToLive: timeToLive ?? TimeSpan.FromSeconds(-1), // Using -1 means that the message does not expire.
                                    visibilityTimeout: initialVisibilityDelay,
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
                        {
                            await queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                        }
                        catch (RequestFailedException e) when (e.ErrorCode != null && e.ErrorCode == "RequestBodyTooLarge")
                        {
                            _logger.LogError(e, "Message sent is too large: {message}", serializedMessage);
                            throw;
                        }
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Messaging.EventGrid;
using Likvido.CloudEvents;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Fallback;
using Polly.Retry;
using CloudEvent = Azure.Messaging.CloudEvent;

namespace Likvido.Azure.EventGrid
{
    public class EventGridService : IEventGridService
    {
        private readonly ILogger<EventGridService> _logger;
        private readonly string _eventGridSource;
        private readonly EventGridPublisherClient _client;

        public EventGridService(EventGridPublisherClient client, ILogger<EventGridService> logger, string eventGridSource)
        {
            _logger = logger;
            _eventGridSource = eventGridSource;
            _client = client;
        }

        public async Task PublishAsync(params IEvent[] events)
        {
            await PublishAsync(LikvidoPriority.Normal, events).ConfigureAwait(false);
        }

        public async Task PublishAsync(LikvidoPriority priority, params IEvent[] events)
        {
            if (events?.Any() != true)
            {
                return;
            }

            if (_client == null)
            {
                throw new InvalidOperationException("EventGridPublisherClient is null. Did you forget to setup DI, by calling the AddAzureEventGridServices extension method?");
            }

            const int sizeLimit = 1536000;
            var currentBatch = new List<CloudEvent>();
            var currentBatchSize = 0;

            foreach (var eventItem in events)
            {
                var cloudEvent = new CloudEvent(_eventGridSource, eventItem.GetEventType(), eventItem)
                {
                    ExtensionAttributes = { ["likvidopriority"] = Enum.GetName(typeof(LikvidoPriority), priority) }
                };

                var eventSize = GetEventSize(cloudEvent);

                if (currentBatchSize + eventSize > sizeLimit)
                {
                    // Send current batch and start a new one
                    await SendBatchAsync(currentBatch).ConfigureAwait(false);
                    currentBatch.Clear();
                    currentBatchSize = 0;
                }

                currentBatch.Add(cloudEvent);
                currentBatchSize += eventSize;
            }

            // Send any remaining events in the final batch
            if (currentBatch.Any())
            {
                await SendBatchAsync(currentBatch).ConfigureAwait(false);
            }
        }

        private static int GetEventSize(CloudEvent cloudEvent)
        {
            // This is a rough estimate of the overhead of the CloudEvent object
            // It was found via experimentation on calling the actual API with various event sizes
            const int eventOverhead = 300;
            var actualEventSize = cloudEvent.Data?.ToArray().Length ?? 0;

            return actualEventSize + eventOverhead;
        }

        private async Task SendBatchAsync(List<CloudEvent> batch)
        {
            await GetResiliencePipeline()
                .ExecuteAsync(async cancellationToken => await _client.SendEventsAsync(batch, cancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        private ResiliencePipeline<Response> GetResiliencePipeline() =>
            new ResiliencePipelineBuilder<Response>()
                .AddRetry(new RetryStrategyOptions<Response>
                {
                    ShouldHandle = new PredicateBuilder<Response>().Handle<Exception>(),
                    Delay = TimeSpan.FromSeconds(2),
                    MaxRetryAttempts = 5,
                    BackoffType = DelayBackoffType.Linear,
                    OnRetry = args =>
                    {
                        _logger.LogError(args.Outcome.Exception, "Error while publishing events to Event Grid. Retrying in {SleepDuration}. Attempt number {AttemptNumber}", args.RetryDelay.ToString("g"), args.AttemptNumber);
                        return default;
                    }
                })
                .AddFallback(new FallbackStrategyOptions<Response>
                {
                    ShouldHandle = new PredicateBuilder<Response>().Handle<Exception>(),
                    FallbackAction = args => default,
                    OnFallback = args =>
                    {
                        if (args.Outcome.Exception == null)
                        {
                            return default;
                        }

                        _logger.LogCritical(args.Outcome.Exception, "Failed to publish events to Event Grid after multiple retries");
                        throw args.Outcome.Exception;
                    }
                })
                .Build();
    }
}

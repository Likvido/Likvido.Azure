using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Azure;
using Azure.Messaging.EventGrid;
using Likvido.Azure.Extensions;
using Likvido.CloudEvents;
using Likvido.Identity.PrincipalProviders;
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
        private readonly IPrincipalProvider _principalProvider;
        private readonly string _eventGridSource;
        private readonly EventGridPublisherClient _client;

        public EventGridService(EventGridPublisherClient client, ILogger<EventGridService> logger, IPrincipalProvider principalProvider, string eventGridSource)
        {
            _logger = logger;
            _principalProvider = principalProvider;
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

                if (_principalProvider.User != null)
                {
                    cloudEvent.ExtensionAttributes.Add("likvidouserclaimsstring", _principalProvider.User.GetAllClaimsAsJsonString());
                }

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
            // This is a rough estimate of the total CloudEvent size used for batching
            // Base overhead was found via experimentation on calling the actual API with various event sizes
            const int eventOverhead = 300;

            // Size of the event data payload (BinaryData)
            var actualEventSize = cloudEvent.Data?.ToArray().Length ?? 0;

            // Include the size of extension attributes as they are serialized and transmitted as part of the event
            var extensionAttributesSize = 0;
            if (cloudEvent.ExtensionAttributes.Count > 0)
            {
                try
                {
                    // Estimate by serializing to UTF-8 JSON (close to wire format size)
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(cloudEvent.ExtensionAttributes);
                    extensionAttributesSize = bytes.Length;
                }
                catch
                {
                    // If serialization fails for any reason, fall back to a minimal estimate based on keys/values length
                    extensionAttributesSize = cloudEvent.ExtensionAttributes.Sum(kvp => (kvp.Key?.Length ?? 0) + (kvp.Value?.ToString()?.Length ?? 0));
                }
            }

            return actualEventSize + extensionAttributesSize + eventOverhead;
        }

        private async Task SendBatchAsync(List<CloudEvent> batch)
        {
            await GetResiliencePipeline()
                .ExecuteAsync(async cancellationToken => await _client.SendEventsAsync(batch, cancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        private ResiliencePipeline<Response> GetResiliencePipeline() =>
            new ResiliencePipelineBuilder<Response>()
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
                .AddRetry(new RetryStrategyOptions<Response>
                {
                    ShouldHandle = new PredicateBuilder<Response>().Handle<Exception>(),
                    Delay = TimeSpan.FromSeconds(2),
                    MaxRetryAttempts = 5,
                    BackoffType = DelayBackoffType.Linear,
                    OnRetry = args =>
                    {
                        _logger.LogWarning(args.Outcome.Exception, "Error while publishing events to Event Grid. Retrying in {SleepDuration}. Attempt number {AttemptNumber}", args.RetryDelay.ToString("g"), args.AttemptNumber);
                        return default;
                    }
                })
                .Build();
    }
}

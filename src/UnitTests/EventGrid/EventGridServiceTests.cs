using Azure;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Likvido.Azure.EventGrid;
using Likvido.Identity.PrincipalProviders;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace UnitTests.EventGrid;

public class EventGridServiceTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(5000, 2)]
    [InlineData(10000, 3)]
    [InlineData(15000, 4)]
    [InlineData(20000, 5)]
    public async Task PublishAsync_WhenSendingManyEvents_TheEventsShouldBeSplitIntoBatches(int eventCount, int batches)
    {
        // Arrange
        var mockClient = new Mock<EventGridPublisherClient>();
        var service = new EventGridService(mockClient.Object, Mock.Of<ILogger<EventGridService>>(), Mock.Of<IPrincipalProvider>(), "fakeSource");
        var events = GenerateFakeEvents(eventCount);

        // Setup
        mockClient
            .Setup(c => c.SendEventsAsync(It.IsAny<IEnumerable<CloudEvent>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new Mock<Response>().Object))
            .Verifiable(Times.Exactly(batches));

        // Act
        await service.PublishAsync(events);

        // Assert
        mockClient.Verify();
    }

    private static IEvent[] GenerateFakeEvents(int count)
    {
        var events = new IEvent[count];
        for (var i = 0; i < count; i++)
        {
            events[i] = new FakeEvent();
        }
        return events;
    }

    private class FakeEvent : IEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string GetEventType() => "FakeEventType";
    }
}

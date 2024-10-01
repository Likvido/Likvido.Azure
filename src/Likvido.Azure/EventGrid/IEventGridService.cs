using System.Threading.Tasks;
using Likvido.CloudEvents;

namespace Likvido.Azure.EventGrid
{
    public interface IEventGridService
    {
        /// <summary>
        /// Will publish the events to the event grid using the "normal" priority
        /// </summary>
        Task PublishAsync(params IEvent[] events);
        Task PublishAsync(LikvidoPriority priority, params IEvent[] events);
    }
}

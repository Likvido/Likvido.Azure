﻿using System;
using Azure;
using Azure.Messaging.EventGrid;
using Azure.Storage.Queues;
using Likvido.Azure.EventGrid;
using Likvido.Azure.Queue;
using Likvido.Azure.Storage;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Likvido.Azure
{
    public static class DependencyInjection
    {
        public static void AddAzureEventGridServices(this IServiceCollection services, EventGridConfiguration eventGridConfiguration)
        {
            if (eventGridConfiguration == null)
            {
                throw new ArgumentNullException(nameof(eventGridConfiguration));
            }

            services.AddAzureClients(builder =>
            {
                builder.AddEventGridPublisherClient(
                    new Uri(eventGridConfiguration.Topic),
                    new AzureKeyCredential(eventGridConfiguration.AccessKey));
            });

            services.AddSingleton<IEventGridService>(sp =>
                new EventGridService(
                    sp.GetService<EventGridPublisherClient>(),
                    sp.GetService<ILogger<EventGridService>>(),
                    eventGridConfiguration.Source));
        }

        public static void AddAzureStorageServices(this IServiceCollection services, StorageConfiguration storageConfiguration)
        {
            if (storageConfiguration == null)
            {
                throw new ArgumentNullException(nameof(storageConfiguration));
            }

            services.AddAzureClients(builder =>
            {
                builder.AddBlobServiceClient(storageConfiguration.ConnectionString);
            });

            services.AddSingleton<IAzureStorageServiceFactory>(_ => new AzureStorageServiceFactory(storageConfiguration));
        }

        public static void AddAzureQueueServices(this IServiceCollection services, QueueConfiguration queueConfiguration)
        {
            if (queueConfiguration == null)
            {
                throw new ArgumentNullException(nameof(queueConfiguration));
            }

            services.AddAzureClients(builder =>
            {
                builder.AddQueueServiceClient(queueConfiguration.ConnectionString)
                    .ConfigureOptions(o => o.MessageEncoding = QueueMessageEncoding.Base64);
            });

            services.AddSingleton<IQueueService, QueueService>();
        }
    }
}

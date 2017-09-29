﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using EventWay.Core;

namespace EventWay.Query
{
    public abstract class Projection
    {
        protected Projection(
            Guid projectionId,
            IEventRepository eventRepository,
            IEventListener eventListener,
            IQueryModelRepository queryModelRepository,
            IProjectionMetadataRepository projectionMetadataRepository)
        {
            if (eventRepository == null) throw new ArgumentNullException(nameof(eventRepository));
            if (eventListener == null) throw new ArgumentNullException(nameof(eventListener));
            if (queryModelRepository == null) throw new ArgumentNullException(nameof(queryModelRepository));
            if (projectionMetadataRepository == null) throw new ArgumentNullException(nameof(projectionMetadataRepository));

            _projectionId = projectionId;

            _eventRepository = eventRepository;
            _eventListener = eventListener;
            QueryModelRepository = queryModelRepository;
            _projectionMetadataRepository = projectionMetadataRepository;

            _eventHandlers = new Dictionary<Type, Func<object, QueryModelStore, Task>>();
        }

        private readonly Guid _projectionId;

        private readonly IEventRepository _eventRepository;
        private readonly IEventListener _eventListener;
        protected readonly IQueryModelRepository QueryModelRepository;
        private readonly IProjectionMetadataRepository _projectionMetadataRepository;

        private readonly Dictionary<Type, Func<object, QueryModelStore, Task>> _eventHandlers;

        public abstract void Listen();

        protected void OnEvent<T>(Func<T, QueryModelStore, Task> handler) where T : class
        {
            if (_eventHandlers.ContainsKey(typeof(T)))
                _eventHandlers[typeof(T)] = (e, queryModelStore) => handler(e as T, queryModelStore);
            else
                _eventHandlers.Add(typeof(T), (e, queryModelStore) => handler(e as T, queryModelStore));

            // Triggers when new events are saved
            _eventListener.OnEvent<T>(async @event =>
            {
                await ProcessEvent(@event);
            });
        }

        private QueryModelStore GetQueryModelStoreFromEvent(OrderedEventPayload @event)
        {
            return new QueryModelStore(
                    QueryModelRepository,
                    _projectionMetadataRepository,
                    @event.Ordering,
                    _projectionId);
        }

        private async Task<int> ProcessEvent(OrderedEventPayload @event)
        {
            var eventPayload = @event.EventPayload;
            var eventType = eventPayload.GetType();

            // Do we have an event listener for this event type?
            // TODO: This can be removed once we only get typed events
            if (!_eventHandlers.ContainsKey(eventType))
                return await Task.FromResult(0);

            var queryModelStore = GetQueryModelStoreFromEvent(@event);

            // Invoke event handler for event
            try
            {
                var eventHandler = _eventHandlers[eventType];
                await eventHandler(@event.EventPayload, queryModelStore);

                return await Task.FromResult(1);
            }
            catch (Exception e)
            {
                //TODO: Handle exception
                Trace.TraceError($"Error while processing event {eventType.FullName} in Projection {this.GetType().FullName}\nException: {e.ToString()}");
                throw;
            }

            //return await Task.FromResult(0);
        }

        protected async Task ProcessEvents<TAggregate>() where TAggregate : Aggregate
        {
            // Get projection metadata
            var projectionMeta = _projectionMetadataRepository.GetByProjectionId(_projectionId);

            // Get event source from Aggregate
            var lastProcessedOffset = projectionMeta.EventOffset;

            // Get all events since lastProcessedOffset belongs to Aggregate
            var events = _eventRepository.GetEvents<TAggregate>(lastProcessedOffset);

            foreach (var @event in events)
            {
                await ProcessEvent(@event);
            }
        }
    }
}

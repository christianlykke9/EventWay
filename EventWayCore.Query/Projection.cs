﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EventWayCore.Core;

namespace EventWayCore.Query
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
			_projectionId = projectionId;

			_eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
			_eventListener = eventListener ?? throw new ArgumentNullException(nameof(eventListener));
			QueryModelRepository = queryModelRepository ?? throw new ArgumentNullException(nameof(queryModelRepository));
			_projectionMetadataRepository = projectionMetadataRepository ?? throw new ArgumentNullException(nameof(projectionMetadataRepository));

			_eventHandlers = new Dictionary<Type, Func<object, QueryModelStore, Task>>();
			_eventCollectionHandlers = new Dictionary<Type, Func<List<object>, QueryModelStore, Task>>();
		}

		private readonly Guid _projectionId;

		private readonly IEventRepository _eventRepository;
		private readonly IEventListener _eventListener;
		protected readonly IQueryModelRepository QueryModelRepository;
		private readonly IProjectionMetadataRepository _projectionMetadataRepository;

		private readonly Dictionary<Type, Func<object, QueryModelStore, Task>> _eventHandlers;
		private readonly Dictionary<Type, Func<List<object>, QueryModelStore, Task>> _eventCollectionHandlers;

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


		protected void OnEvents<T>(Func<List<T>, QueryModelStore, Task> handler) where T : class
		{
			if (_eventCollectionHandlers.ContainsKey(typeof(T)))
				_eventCollectionHandlers[typeof(T)] = (e, queryModelStore) => handler(((IList)e).Cast<T>().ToList(), queryModelStore);
			else
				_eventCollectionHandlers.Add(typeof(T), (e, queryModelStore) => handler(((IList)e).Cast<T>().ToList(), queryModelStore));

			// Triggers when new events are saved
			_eventListener.OnEvents<T>(async @event =>
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

		private async Task<int> ProcessEvent(OrderedEventPayload[] @event)
		{
			var eventPayloads = @event.Select(x => x.EventPayload).ToList();
			var eventType = eventPayloads[0].GetType();

			// Do we have an event listener for this event type?
			// TODO: This can be removed once we only get typed events
			if (!_eventCollectionHandlers.ContainsKey(eventType))
				return await Task.FromResult(0);

			var queryModelStore = GetQueryModelStoreFromEvent(@event.FirstOrDefault(x => x.Ordering == @event.Max(p => p.Ordering)));

			// Invoke event handler for event
			try
			{

				var eventHandler = _eventCollectionHandlers[eventType];
				await eventHandler(eventPayloads, queryModelStore);

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
			if (lastProcessedOffset <= 0)
			{
				return;
			}

			// Get all events since lastProcessedOffset belongs to Aggregate
			var events = _eventRepository.GetEvents<TAggregate>(lastProcessedOffset);

			foreach (var @event in events)
			{
				await ProcessEvent(@event);
			}
		}
	}
}

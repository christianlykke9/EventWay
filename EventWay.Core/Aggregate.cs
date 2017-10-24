﻿using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EventWay.Core
{
    public abstract class Aggregate : IAggregate
    {
        public virtual int SnapshotSize => 50;

        public Guid Id { get; set; }
        public int Version { get; set; }

        private readonly List<object> _uncommittedEvents;

        private readonly Dictionary<Type, Func<object, object>> _commandHandlers;
        private readonly Dictionary<Type, Action<object>> _eventHandlers;

        protected Aggregate(Guid id)
        {
            Id = id;

            _uncommittedEvents = new List<object>();

            _commandHandlers = new Dictionary<Type, Func<object, object>>();
            _eventHandlers = new Dictionary<Type, Action<object>>();
        }

        protected void OnCommand<T>(Action<T> handler) where T : class
        {
            if (_commandHandlers.ContainsKey(typeof(T)))
                _commandHandlers[typeof(T)] = (e) => {
                    handler(e as T);
                    return null;
                };
            else
                _commandHandlers.Add(typeof(T), (e) => {
                    handler(e as T);
                    return null;
                });
        }

        protected void OnCommand<T, TResult>(Func<T, TResult> handler) where T : class
        {
            if (_commandHandlers.ContainsKey(typeof(T)))
                _commandHandlers[typeof(T)] = (e) => handler(e as T);
            else
                _commandHandlers.Add(typeof(T), (e) => handler(e as T));
        }

        protected void OnEvent<T>(Action<T> handler) where T : class
        {
            if (_eventHandlers.ContainsKey(typeof(T)))
                _eventHandlers[typeof(T)] = (e) => handler(e as T);
            else
                _eventHandlers.Add(typeof(T), (e) => handler(e as T));
        }

        public List<object> GetUncommittedEvents()
        {
            return _uncommittedEvents;
        }

        public void ClearUncommittedEvents()
        {
            _uncommittedEvents.Clear();
        }

        public void Apply(object @event)
        {
            Version++;

            var eventType = @event.GetType();

            if (!_eventHandlers.ContainsKey(eventType))
            {
                Trace.TraceWarning($"No Event Handler for type: {eventType.FullName}");
                UnhandledEvent(@event);
                return;
            }

            _eventHandlers[eventType](@event);
        }

        public void Apply(object[] events)
        {
            foreach (var @event in events)
                Apply(@event);
        }

        protected void Publish(object @event)
        {
            // Store and apply event
            _uncommittedEvents.Add(@event);
            Apply(@event);

            if (@event is SnapshotOffer)
                return;

            int numSnapshots = (Version - 1) / SnapshotSize;
            if ((Version - numSnapshots) % SnapshotSize == 0)
                SaveSnapshot();
        }

        private void SaveSnapshot()
        {
            var state = GetState();
            if (state == null)
                return;

            var snapshotEvent = new SnapshotOffer()
            {
                State = state
            };

            Publish(snapshotEvent);
        }

        public void Tell(IDomainCommand command)
        {
            var commandType = AssertCommandHandler(command);

            _commandHandlers[commandType](command);
        }

        public T Ask<T>(IDomainCommand command)
        {
            var commandType = AssertCommandHandler(command);

            return (T)_commandHandlers[commandType](command);
        }

        private Type AssertCommandHandler(IDomainCommand command)
        {
            // Get command type and throw error if command has no handler in aggregate
            var commandType = command.GetType();
            if (!_commandHandlers.ContainsKey(commandType))
                throw new MissingMethodException($"Command of type {command.GetType()}. not handled");

            return commandType;
        }

        /// <summary>
        /// If no event handler was specified for a given event type. UnhandledEvent will be called.
        /// </summary>
        /// <param name="event"></param>
        protected virtual void UnhandledEvent(object @event) {}

        protected virtual object GetState()
        {
            return null;
        }
    }
}
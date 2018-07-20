﻿using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EventWayCore.Core
{
    public static class ExtendsEvent
    {
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            ContractResolver = new ShouldSerializeContractResolver()
        };

        public static OrderedEventPayload DeserializeOrderedEvent(this Event x)
        {
            return new OrderedEventPayload(x.Version, x.DeserializeEvent());
        }

        public static object DeserializeEvent(this Event x)
        {
            var eventType = Type.GetType(x.EventType);
            var deserializedPayload = JsonConvert.DeserializeObject(x.Payload, eventType);

            if (deserializedPayload.GetType().IsSubclassOf(typeof(DomainEvent)))
            {
                ((DomainEvent) deserializedPayload).AggregateId = x.AggregateId;
                ((DomainEvent) deserializedPayload).AggregateType = x.AggregateType;
            }

            return deserializedPayload;
        }

        public static Event ToEventData(this object @event, string aggregateType, Guid aggregateId, int version)
        {
            string metadata = null;

            var data = JsonConvert.SerializeObject(@event, SerializerSettings);
            var eventId = CombGuid.Generate();

            var eventType = @event.GetType().AssemblyQualifiedName;

            return new Event
            {
                EventId = eventId,
                Created = DateTime.UtcNow,
                EventType = eventType,
                AggregateType = aggregateType,
                AggregateId = aggregateId,
                Version = version,
                Payload = data,
                Metadata = metadata,
            };
        }

        public class ShouldSerializeContractResolver : DefaultContractResolver
        {
            public static readonly ShouldSerializeContractResolver Instance = new ShouldSerializeContractResolver();

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);

                property.ShouldSerialize = instance => true;

                if (property.PropertyName == "AggregateId" ||
                    property.PropertyName == "AggregateType")
                    property.ShouldSerialize = instance => false;

                return property;
            }
        }
    }
}

﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using Dapper;
using EventWay.Core;

namespace EventWay.Infrastructure.MsSql
{
    public class SqlServerSnapshotEventRepository : ISnapshotEventRepository
    {
        private const int CommandTimeout = 600;

        private readonly string _connectionString;

        public SqlServerSnapshotEventRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        private SqlConnection Connect() => new SqlConnection(_connectionString).AsOpen();

        public List<OrderedEventPayload> GetSnapshotEventsByAggregateId(Guid aggregateId)
        {
            using (var conn = Connect())
            {
                const string sql = "SELECT * FROM SnapshotEvents WHERE AggregateId=@aggregateId";

                var listOfEventData = conn.Query<Event>(sql, new { aggregateId }, commandTimeout: CommandTimeout);

                var events = listOfEventData
                    .Select(x => x.DeserializeOrderedEvent())
                    .ToList();

                return events;
            }
        }

        public object GetSnapshotEventByAggregateIdAndVersion(Guid aggregateId, int version)
        {
            using (var conn = Connect())
            {
                const string sql = "SELECT * FROM SnapshotEvents WHERE AggregateId=@aggregateId And Version=@version";

                var listOfEventData = conn.Query<Event>(sql, new { aggregateId, version }, commandTimeout: CommandTimeout);

                var events = listOfEventData
                    .Select(x => x.DeserializeOrderedEvent())
                    .ToList();

                return events.First().EventPayload;
            }
        }

        public int? GetVersionByAggregateId(Guid aggregateId)
        {
            using (var conn = Connect())
            {
                const string sql = "SELECT MAX(Version) FROM SnapshotEvents WHERE AggregateId=@aggregateId";

                var version = (int?)conn.ExecuteScalar(sql, new { aggregateId }, commandTimeout: CommandTimeout);

                return version;
            }
        }

        public void SaveSnapshotEvent(Event snapshotEvent)
        {
            SaveSnapshotEvents(new []{snapshotEvent});
        }

        public void SaveSnapshotEvents(Event[] snapshotEvents)
        {
            if (!snapshotEvents.Any())
                return;

            var bulk = new BulkCopyTools(_connectionString, "SnapshotEvents");
            bulk.BulkInsertEvents(snapshotEvents);
        }

        public void ClearSnapshotEventsByAggregateId(Guid aggregateId, int to)
        {
            using (var conn = Connect())
            {
                const string sql = "DELETE FROM SnapshotEvents WHERE AggregateId=@aggregateId And Version < @to";
                conn.Execute(sql, new {aggregateId, to}, commandTimeout: CommandTimeout);
            }
        }

        public void ClearSnapshotEventsByAggregateId(Guid aggregateId)
        {
            ClearSnapshotEventsByAggregateId(aggregateId, int.MaxValue);
        }

        public void ClearSnapshotEvents()
        {
            using (var conn = Connect())
            {
                const string sql = "TRUNCATE TABLE SnapshotEvents";
                conn.Execute(sql, commandTimeout: CommandTimeout);
            }
        }
    }
}
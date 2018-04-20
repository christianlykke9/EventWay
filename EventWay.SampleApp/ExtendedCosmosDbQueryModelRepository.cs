﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using EventWay.Infrastructure.CosmosDb;
using EventWay.Query;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;

namespace EventWay.SampleApp
{
    public class ExtendedCosmosDbQueryModelRepository : IExtendedQueryModelRepository
    {
        private readonly CosmosDbQueryModelRepository _repo;

        public ExtendedCosmosDbQueryModelRepository(CosmosDbQueryModelRepository repo)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            _repo = repo;
        }

        private FeedOptions CreateFeedOptions(int maxItemCount, bool enableCrossPartitionQuery = true)
        {
            return new FeedOptions
            {
                MaxItemCount = maxItemCount,
                EnableCrossPartitionQuery = enableCrossPartitionQuery,
                MaxDegreeOfParallelism = -1,
                MaxBufferedItemCount = -1
            };
        }

        public async Task<List<dynamic>> GetRawSql(string sql)
        {
            return await DocumentDbRetryPolicy.ExecuteWithRetries(
                () => ExecuteRawSqlInternal(sql)
                );
        }

        private async Task<List<dynamic>> ExecuteRawSqlInternal(string sql)
        {
            var options = CreateFeedOptions(-1);
            var query = _repo.GetClient().CreateDocumentQuery<dynamic>(_repo.GetCollectionUri(), sql, options).AsDocumentQuery();
            var result = await query.ExecuteNextAsync<dynamic>();
            return result.ToList();
        }

        public async Task<PagedResult<T>> GetPagedList<T>(PagedQuery pagedQuery) where T : QueryModel
        {
            return await GetPagedList<T>(pagedQuery, null);
        }

        public async Task<PagedResult<T>> GetPagedList<T>(PagedQuery pagedQuery, Expression<Func<T, bool>> predicate) where T : QueryModel
        {
            return await DocumentDbRetryPolicy.ExecuteWithRetries(
              () => GetPagedListAsyncInternal<T>(pagedQuery, predicate)
              );
        }

        private async Task<PagedResult<T>> GetPagedListAsyncInternal<T>(PagedQuery pagedQuery, Expression<Func<T, bool>> predicate) where T : QueryModel
        {
            var query = CreatePagedListQuery(pagedQuery, predicate);

            var results = await query.AsDocumentQuery().ExecuteNextAsync<T>();
            //Should not use count in this way, currently Cosmos does not support count using group by, it will make query very slow when db was large
            //var count = await QueryCountAsyncInternal<T>();
            var count = 0;
            return new PagedResult<T>(results.ToList().AsReadOnly(), count, results.ResponseContinuation);
        }

        private IQueryable<T> CreatePagedListQuery<T>(PagedQuery pagedQuery, Expression<Func<T, bool>> predicate) where T : QueryModel
        {
            var options = CreateFeedOptions(pagedQuery.MaxItemCount);
            if (!string.IsNullOrEmpty(pagedQuery.ContinuationToken))
            {
                options.RequestContinuation = pagedQuery.ContinuationToken;
            }

            var query = _repo.GetClient().CreateDocumentQuery<T>(_repo.GetCollectionUri(), options)
                .Where(x => x.Type == typeof(T).Name);
            if (predicate != null)
            {
                query = query.Where(predicate);
            }

            return query;
        }

        //////////////////////////////////////////////////
        // CosmosDbQueryModelRepository methods
        //////////////////////////////////////////////////
        public Task Save(QueryModel queryModel)
        {
            return _repo.Save(queryModel);
        }

        public Task<T> GetById<T>(Guid aggregateId) where T : QueryModel
        {
            return _repo.GetById<T>(aggregateId);
        }

        public Task<IEnumerable<T>> GetAll<T>() where T : QueryModel
        {
            return _repo.GetAll<T>();
        }

        public Task<T> QueryItem<T>(Expression<Func<T, bool>> predicate) where T : QueryModel
        {
            return _repo.QueryItem<T>(predicate);
        }

        public Task<IEnumerable<T>> QueryAll<T>(Expression<Func<T, bool>> predicate) where T : QueryModel
        {
            return _repo.QueryAll<T>(predicate);
        }

        public Task DeleteById<T>(Guid aggregateId) where T : QueryModel
        {
            return _repo.DeleteById<T>(aggregateId);
        }

        public Task DeleteByIds<T>(List<Guid> ids) where T : QueryModel
        {
            throw new NotImplementedException();
        }

        public Task Save<T>(List<T> queryModels) where T : QueryModel
        {
            throw new NotImplementedException();
        }

        public Task<List<T>> GetByIds<T>(List<Guid> aggregateIds) where T : QueryModel
        {
            throw new NotImplementedException();
        }

        public Task<bool> DoesItemExist<T>(Guid id) where T : QueryModel
        {
            throw new NotImplementedException();
        }

        public Task<bool> DoesItemExist<T>(Expression<Func<T, bool>> predicate) where T : QueryModel
        {
            throw new NotImplementedException();
        }

        public Task<List<dynamic>> ExecuteRawSql(string sql)
        {
            throw new NotImplementedException();
        }

        public Task ClearCollectionAsync()
        {
            throw new NotImplementedException();
        }
    }
}

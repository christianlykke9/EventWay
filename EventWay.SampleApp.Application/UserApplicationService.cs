﻿using System;
using System.Threading.Tasks;
using EventWay.Core;
using EventWay.Query;
using EventWay.SampleApp.Application.QueryModels;
using EventWay.SampleApp.Core;
using EventWay.SampleApp.Core.Commands;

namespace EventWay.SampleApp.Application
{
    public class UserApplicationService
    {
        public UserApplicationService(
            IAggregateStore aggregateStore,
            IQueryModelRepository queryModelRepository)
        {
            if (aggregateStore == null) throw new ArgumentNullException(nameof(aggregateStore));
            if (queryModelRepository == null) throw new ArgumentNullException(nameof(queryModelRepository));

            _aggregateStore = aggregateStore;
            _queryModelRepository = queryModelRepository;
        }

        private readonly IAggregateStore _aggregateStore;
        private readonly IQueryModelRepository _queryModelRepository;

        public async Task<Guid> RegisterUser(Commands.RegisterUser command)
        {
            // Check if user already exists
            var existingUser = await _queryModelRepository.QueryItem<UserQueryModel>(x => x.DisplayName == $"{command.FirstName} {command.LastName}");
            if (existingUser != null)
                return existingUser.id;

            // Create aggregate
            var newUserId = Guid.NewGuid();
            var user = new User(newUserId);

            // Create domain command
            var domainCommand = new Core.Commands.RegisterUser(
                command.FirstName,
                command.LastName);

            // Fire command and wait for status response
            user.Tell(domainCommand);

            // Save User aggregate
            // Note: This saves the events published internally by the User Aggregate)
            await _aggregateStore.Save(user);

            return newUserId;
        }
    }
}

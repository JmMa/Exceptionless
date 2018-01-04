﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Repositories;
using Foundatio.Caching;
using Foundatio.Utility;
using Microsoft.Extensions.Logging;

namespace Exceptionless.Core.Services {
    public class StackService {
        private readonly ILogger<UsageService> _logger;
        private readonly IStackRepository _stackRepository;
        private readonly ICacheClient _cache;

        public StackService(IStackRepository stackRepository, ICacheClient cache, ILoggerFactory loggerFactory = null) {
            _stackRepository = stackRepository;
            _cache = cache;
            _logger = loggerFactory.CreateLogger<UsageService>();
        }

        public async Task IncrementStackUsageAsync(string organizationId, string projectId, string stackId, DateTime minOccurrenceDateUtc, DateTime maxOccurrenceDateUtc, int count) {
            if (String.IsNullOrEmpty(organizationId) || String.IsNullOrEmpty(projectId) || String.IsNullOrEmpty(stackId) || count == 0)
                return;
            var expireTimeout = TimeSpan.FromDays(1);
            var tasks = new List<Task>(4);

            string occurenceCountCacheKey = GetStackOccurrenceCountCacheKey(organizationId, projectId, stackId),
                occurrenceMinDateCacheKey = GetStackOccurrenceMinDateCacheKey(organizationId, projectId, stackId),
                occurrenceMaxDateCacheKey = GetStackOccurrenceMaxDateCacheKey(organizationId, projectId, stackId),
                occurrenceSetCacheKey = GetStackOccurrenceSetCacheKey();

            var cachedOccurrenceMinDateUtc = await _cache.GetAsync<DateTime>(occurrenceMinDateCacheKey).AnyContext();
            if (!cachedOccurrenceMinDateUtc.HasValue || cachedOccurrenceMinDateUtc.IsNull || cachedOccurrenceMinDateUtc.Value > minOccurrenceDateUtc)
                tasks.Add(_cache.SetAsync(occurrenceMinDateCacheKey, minOccurrenceDateUtc, expireTimeout));

            var cachedOccurrenceMaxDateUtc = await _cache.GetAsync<DateTime>(occurrenceMaxDateCacheKey).AnyContext();
            if (!cachedOccurrenceMaxDateUtc.HasValue || cachedOccurrenceMinDateUtc.IsNull || cachedOccurrenceMaxDateUtc.Value < maxOccurrenceDateUtc)
                tasks.Add(_cache.SetAsync(occurrenceMaxDateCacheKey, maxOccurrenceDateUtc, expireTimeout));

            tasks.Add(_cache.IncrementAsync(occurenceCountCacheKey, count, expireTimeout));
            tasks.Add(_cache.SetAddAsync(occurrenceSetCacheKey, Tuple.Create(organizationId, projectId, stackId), expireTimeout));

            await Task.WhenAll(tasks).AnyContext();
        }

        public async Task SaveStackUsagesAsync(bool sendNotifications = true, CancellationToken cancellationToken = default) {
            var occurrenceSetCacheKey = GetStackOccurrenceSetCacheKey();
            var stackUsageSet = await _cache.GetSetAsync<Tuple<string, string, string>>(occurrenceSetCacheKey).AnyContext();
            if (!stackUsageSet.HasValue || stackUsageSet.IsNull) return;
            foreach (var tuple in stackUsageSet.Value) {
                if (cancellationToken.IsCancellationRequested) break;

                string organizationId = tuple.Item1, projectId = tuple.Item2, stackId = tuple.Item3;
                string occurenceCountCacheKey = GetStackOccurrenceCountCacheKey(organizationId, projectId, stackId),
                    occurrenceMinDateCacheKey = GetStackOccurrenceMinDateCacheKey(organizationId, projectId, stackId),
                    occurrenceMaxDateCacheKey = GetStackOccurrenceMaxDateCacheKey(organizationId, projectId, stackId);
                var occurrenceCount = await _cache.GetAsync<long>(occurenceCountCacheKey, 0).AnyContext();
                if (occurrenceCount == 0) return;
                var occurrenceMinDate = _cache.GetAsync(occurrenceMinDateCacheKey, SystemClock.UtcNow);
                var occurrenceMaxDate = _cache.GetAsync(occurrenceMaxDateCacheKey, SystemClock.UtcNow);

                await Task.WhenAll(occurrenceMinDate, occurrenceMaxDate).AnyContext();

                var tasks = new List<Task> {
                    _stackRepository.IncrementEventCounterAsync(organizationId, projectId, stackId, occurrenceMinDate.Result, occurrenceMaxDate.Result, (int)occurrenceCount, sendNotifications),
                    _cache.RemoveAllAsync(new[] { occurenceCountCacheKey, occurrenceMinDateCacheKey, occurrenceMaxDateCacheKey }),
                    _cache.SetRemoveAsync(occurrenceSetCacheKey, tuple, TimeSpan.FromHours(24))
                };
                await Task.WhenAll(tasks).AnyContext();

                _logger.LogTrace($"Increment event count {occurrenceCount} for organization:{organizationId} project:{projectId} stack:{stackId} with occurrenceMinDate:{occurrenceMinDate.Result} occurrenceMaxDate:{occurrenceMaxDate.Result}");
            }
        }

        private string GetStackOccurrenceSetCacheKey() {
            return "usage:occurrences";
        }

        private string GetStackOccurrenceCountCacheKey(string organizationId, string projectId, string stackId) {
            return $"usage:occurrences:count:{organizationId}:{projectId}:{stackId}";
        }

        private string GetStackOccurrenceMinDateCacheKey(string organizationId, string projectId, string stackId) {
            return $"usage:occurrences:mindate:{organizationId}:{projectId}:{stackId}";
        }

        private string GetStackOccurrenceMaxDateCacheKey(string organizationId, string projectId, string stackId) {
            return $"usage:occurrences:maxdate:{organizationId}:{projectId}:{stackId}";
        }
    }
}

namespace Prague.Api;

using Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Kafka;
using Kafka.Options;

public class DataCachesEndpointsMapper: ICacheMapper<IEndpointRouteBuilder> {
	public static IEndpointRouteBuilder CacheMap<TCache, TKey, TValue>(IEndpointRouteBuilder endpoints)
		where TCache : class, IDataCache<TKey, TValue>
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		// Map GET by ID endpoint if the key is parsable
		if (TCache.IsKeyParsable)
			endpoints.MapGet($"{typeof(TCache).Name}/" + "{id}",
				(IDataCacheRegistry registry, [FromRoute] TKey id) => {
					var cache = registry.GetCache<TCache>();
					return !cache.TryGet(id, out var item) ? Results.NotFound() : Results.Json(item);
				});

		// Map GET collection endpoint with query string support
		endpoints.MapGet($"{typeof(TCache).Name}",
			async (IDataCacheRegistry registry, HttpRequest request, HttpResponse response) => {
				var cache = registry.GetCache<TCache>();
				var queryString = request.QueryString.Value;

				// If no query string, return first 100 items
				if (string.IsNullOrEmpty(queryString)) {
					var results = cache.QueryParser.StringQueryPooled("");
					try {
						await response.WriteAsJsonAsync(results);
					}
					finally {
						results.Dispose();
					}

					return;
				}

				// Use the generated QueryParser to parse and execute the query
				var queryResults = cache.QueryParser.StringQueryPooled(queryString);
				try {
					await response.WriteAsJsonAsync(queryResults);
				}
				finally {
					queryResults.Dispose();
				}
			});

		return endpoints;
	}
}

public static class DataCachesEndpointsExtensions {
	public static IEndpointRouteBuilder MapPragueEndpoints(this IEndpointRouteBuilder endpoints) {
		var opts = endpoints.ServiceProvider.GetRequiredService<IOptions<KafkaCachesGlobalOptions>>();
		if (opts.Value.EndpointsMapped)
			return endpoints;

		opts.Value.EndpointsMapped = true;
		opts.Value.StatisticsEnabled = true;

		endpoints.MapGet("/prague", (KafkaCachesStatistics stats) =>
			Results.Json(stats, KafkaCachesJsonContext.Default.KafkaCachesStatistics));

		var registry = endpoints.ServiceProvider.GetRequiredService<IDataCacheRegistry>();
		var builder = endpoints.MapGroup("/prague");
		registry.MapAll<DataCachesEndpointsMapper, IEndpointRouteBuilder>(builder);
		return endpoints;
	}
}

using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace MWDashboard.Web.Services;

/// <summary>
/// Uses Redis pub/sub to propagate cache invalidation across multiple Web replicas.
/// When a Save* method invalidates a cache key on one replica, all other replicas
/// receive the notification and remove their local copies.
/// </summary>
public class RedisCacheInvalidationService : IDisposable
{
    private const string Channel = "MWDashboard:cache-invalidation";
    private readonly IConnectionMultiplexer? _redis;
    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisCacheInvalidationService> _logger;
    private ISubscriber? _subscriber;

    public RedisCacheInvalidationService(
        IDistributedCache cache,
        ILogger<RedisCacheInvalidationService> logger,
        IConnectionMultiplexer? redis = null)
    {
        _cache = cache;
        _logger = logger;
        _redis = redis;

        if (_redis != null)
        {
            _subscriber = _redis.GetSubscriber();
            _subscriber.Subscribe(RedisChannel.Literal(Channel), OnInvalidationMessage);
            _logger.LogInformation("Redis pub/sub cache invalidation subscribed");
        }
    }

    /// <summary>
    /// Publish a cache key invalidation to all replicas.
    /// </summary>
    public async Task PublishInvalidationAsync(params string[] keys)
    {
        if (_subscriber == null) return;

        foreach (var key in keys)
        {
            try
            {
                await _subscriber.PublishAsync(RedisChannel.Literal(Channel), key);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to publish invalidation for key {Key}", key);
            }
        }
    }

    private void OnInvalidationMessage(RedisChannel channel, RedisValue message)
    {
        var key = message.ToString();
        if (string.IsNullOrEmpty(key)) return;

        try
        {
            _cache.Remove(key);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to remove cache key {Key} from invalidation message", key);
        }
    }

    public void Dispose()
    {
        _subscriber?.UnsubscribeAll();
    }
}

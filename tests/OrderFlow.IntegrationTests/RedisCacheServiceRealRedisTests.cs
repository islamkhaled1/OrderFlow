using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OrderFlow.Infrastructure.Caching;
using OrderFlow.Infrastructure.Observability;
using StackExchange.Redis;
using System.Diagnostics.Metrics;
using Xunit;

namespace OrderFlow.IntegrationTests;

/// <summary>Runs only when ORDERFLOW_TEST_REDIS is set (e.g. localhost:6379); otherwise reported as skipped.</summary>
public sealed class RedisFactAttribute : FactAttribute
{
    public const string EnvVar = "ORDERFLOW_TEST_REDIS";

    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Set {EnvVar} (e.g. localhost:6379) to run tests against a real Redis server.";
    }
}

public class RedisCacheServiceRealRedisTests : IAsyncLifetime
{
    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }

    private sealed record Dto(int Id, string Name);

    private IConnectionMultiplexer? _mux;
    private readonly string _prefix = $"orderflow-test:{Guid.NewGuid():N}:";

    public async Task InitializeAsync()
    {
        var conn = Environment.GetEnvironmentVariable(RedisFactAttribute.EnvVar);
        if (string.IsNullOrWhiteSpace(conn)) return;
        var options = ConfigurationOptions.Parse(conn);
        options.AbortOnConnectFail = false;
        options.AsyncTimeout = 1000;
        _mux = await ConnectionMultiplexer.ConnectAsync(options);
    }

    public async Task DisposeAsync()
    {
        if (_mux is not null) await _mux.DisposeAsync();
    }

    private RedisCacheService Sut() =>
        new(NullLogger<RedisCacheService>.Instance, new InfrastructureMetrics(new TestMeterFactory()), _mux);

    [RedisFact]
    public async Task Set_get_and_remove_round_trip_against_real_redis()
    {
        var sut = Sut();
        var key = _prefix + "order:1";

        await sut.SetAsync(key, new Dto(1, "a"), TimeSpan.FromMinutes(1));
        (await sut.GetAsync<Dto>(key)).Should().Be(new Dto(1, "a"));

        (await sut.RemoveAsync(key)).Should().BeTrue();
        (await sut.GetAsync<Dto>(key)).Should().BeNull();
    }

    [RedisFact]
    public async Task Entries_expire_with_the_configured_ttl()
    {
        var sut = Sut();
        var key = _prefix + "ttl";

        await sut.SetAsync(key, new Dto(2, "b"), TimeSpan.FromMilliseconds(300));
        (await sut.GetAsync<Dto>(key)).Should().NotBeNull();

        await Task.Delay(700);
        (await sut.GetAsync<Dto>(key)).Should().BeNull();
    }

    [RedisFact]
    public async Task Corrupt_value_written_by_another_client_is_dropped_as_a_miss()
    {
        var key = _prefix + "corrupt";
        await _mux!.GetDatabase().StringSetAsync(key, "{not json", TimeSpan.FromMinutes(1));

        (await Sut().GetAsync<Dto>(key)).Should().BeNull();
        (await _mux.GetDatabase().KeyExistsAsync(key)).Should().BeFalse();
    }

    [RedisFact]
    public async Task Removing_a_missing_key_is_still_a_success()
    {
        (await Sut().RemoveAsync(_prefix + "never-existed")).Should().BeTrue();
    }
}

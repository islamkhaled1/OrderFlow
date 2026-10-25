using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderFlow.Infrastructure.Caching;
using OrderFlow.Infrastructure.Observability;
using StackExchange.Redis;
using Xunit;

namespace OrderFlow.UnitTests.Infrastructure;

/// <summary>
/// Adapter-level tests with a mocked multiplexer (failure handling, no I/O). Behaviour against a real
/// Redis server is covered by the opt-in <c>RedisCacheServiceRealRedisTests</c> integration tests.
/// </summary>
public class RedisCacheServiceTests
{
    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }

    private sealed record Dto(int Id, string Name);

    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _db = new();

    private RedisCacheService CreateSut(bool connected = true, bool withMultiplexer = true)
    {
        _redis.SetupGet(r => r.IsConnected).Returns(connected);
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_db.Object);
        return new RedisCacheService(
            NullLogger<RedisCacheService>.Instance,
            new InfrastructureMetrics(new TestMeterFactory()),
            withMultiplexer ? _redis.Object : null);
    }

    [Fact]
    public async Task Get_returns_deserialized_value_on_hit()
    {
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"{\"id\":7,\"name\":\"x\"}");

        var result = await CreateSut().GetAsync<Dto>("order:7");

        result.Should().Be(new Dto(7, "x"));
    }

    [Fact]
    public async Task Get_returns_default_on_miss()
    {
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        (await CreateSut().GetAsync<Dto>("order:7")).Should().BeNull();
    }

    [Fact]
    public async Task Disconnected_multiplexer_degrades_without_touching_redis()
    {
        var sut = CreateSut(connected: false);

        (await sut.GetAsync<Dto>("k")).Should().BeNull();
        await sut.SetAsync("k", new Dto(1, "a"), TimeSpan.FromMinutes(1));
        (await sut.RemoveAsync("k")).Should().BeFalse();

        _redis.Verify(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task Missing_multiplexer_degrades_like_an_outage()
    {
        var sut = CreateSut(withMultiplexer: false);

        (await sut.GetAsync<Dto>("k")).Should().BeNull();
        (await sut.RemoveAsync("k")).Should().BeFalse();
    }

    [Fact]
    public async Task Redis_connection_failure_on_get_is_a_miss()
    {
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

        (await CreateSut().GetAsync<Dto>("k")).Should().BeNull();
    }

    [Fact]
    public async Task Redis_timeout_on_set_is_swallowed()
    {
        _db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new TimeoutException());

        var act = () => CreateSut().SetAsync("k", new Dto(1, "a"), TimeSpan.FromMinutes(1));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Remove_reports_false_when_redis_fails_so_callers_can_retry()
    {
        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisTimeoutException("slow", CommandStatus.Unknown));

        (await CreateSut().RemoveAsync("k")).Should().BeFalse();
    }

    [Fact]
    public async Task Remove_reports_true_on_success()
    {
        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);

        (await CreateSut().RemoveAsync("k")).Should().BeTrue();
    }

    [Fact]
    public async Task Corrupt_json_is_treated_as_a_miss_and_the_entry_is_removed()
    {
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"{not json");
        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);

        (await CreateSut().GetAsync<Dto>("k")).Should().BeNull();

        _db.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task Programming_errors_are_not_swallowed()
    {
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new InvalidOperationException("bug"));

        var act = () => CreateSut().GetAsync<Dto>("k");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_looking_like_a_cache_miss()
    {
        var never = new TaskCompletionSource<RedisValue>();
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(never.Task);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => CreateSut().GetAsync<Dto>("k", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Blank_keys_are_ignored()
    {
        var sut = CreateSut();

        (await sut.GetAsync<Dto>(" ")).Should().BeNull();
        await sut.SetAsync(" ", new Dto(1, "a"), TimeSpan.FromMinutes(1));
        (await sut.RemoveAsync("")).Should().BeTrue();

        _redis.Verify(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()), Times.Never);
    }
}

using FluentAssertions;
using OrderFlow.Infrastructure.Outbox;
using Xunit;

namespace OrderFlow.UnitTests.Infrastructure;

public class OutboxBackoffTests
{
    private static readonly OutboxOptions Defaults = new(); // base 5s, cap 300s

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    [InlineData(5, 80)]
    [InlineData(6, 160)]
    [InlineData(7, 300)]   // 320 capped
    [InlineData(8, 300)]
    public void Backoff_doubles_each_attempt_and_is_capped(int attempt, int expectedSeconds)
    {
        OutboxProcessor.ComputeBackoff(attempt, Defaults).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void Backoff_never_overflows_for_huge_attempt_counts()
    {
        OutboxProcessor.ComputeBackoff(int.MaxValue, Defaults).Should().Be(TimeSpan.FromSeconds(300));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Backoff_treats_non_positive_attempts_as_the_first(int attempt)
    {
        OutboxProcessor.ComputeBackoff(attempt, Defaults).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Backoff_honours_custom_options()
    {
        var options = new OutboxOptions { BaseBackoffSeconds = 1, MaxBackoffSeconds = 10 };

        OutboxProcessor.ComputeBackoff(3, options).Should().Be(TimeSpan.FromSeconds(4));
        OutboxProcessor.ComputeBackoff(10, options).Should().Be(TimeSpan.FromSeconds(10));
    }
}

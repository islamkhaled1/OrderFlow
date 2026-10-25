namespace OrderFlow.Infrastructure.BackgroundServices;

public class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    /// <summary>Interval of the maintenance cycle (order expiry + retention purges).</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Pending orders older than this are cancelled and their stock released. 0 disables expiry.</summary>
    public int PendingOrderExpirationMinutes { get; set; } = 60;

    public int ExpirationBatchSize { get; set; } = 100;
}

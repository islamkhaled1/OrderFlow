namespace OrderFlow.Infrastructure.BackgroundServices;

public class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    public int IntervalSeconds { get; set; } = 30;
}

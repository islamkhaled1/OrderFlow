namespace OrderFlow.Application.Common.Options;

public class CachingOptions
{
    public const string SectionName = "Caching";

    public int OrderDetailsExpirationMinutes { get; set; } = 5;
}

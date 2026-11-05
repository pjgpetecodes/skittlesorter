namespace skittle_sorter;

public sealed class SessionCounts
{
    private readonly Dictionary<string, long> _counts = new(StringComparer.OrdinalIgnoreCase);
    public long Total { get; private set; }
    public IReadOnlyDictionary<string, long> PerColour => _counts;

    public void RecordCompletedDrop(string colour)
    {
        if (string.IsNullOrWhiteSpace(colour) ||
            string.Equals(colour, "None", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only a completed, identified Skittle can be counted.", nameof(colour));

        var key = colour.ToLowerInvariant();
        _counts[key] = checked(_counts.GetValueOrDefault(key) + 1);
        Total = checked(Total + 1);
    }
}

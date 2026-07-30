namespace Nofarma.Domain.Inventory;

public readonly record struct ExpiryDate
{
    private ExpiryDate(int year, int month, int? day, DateOnly blockingDate)
    {
        Year = year;
        Month = month;
        Day = day;
        BlockingDate = blockingDate;
    }

    public int Year { get; }

    public int Month { get; }

    public int? Day { get; }

    public DateOnly BlockingDate { get; }

    public bool HasExactDay => Day.HasValue;

    public static ExpiryDate ForMonth(int year, int month)
    {
        try
        {
            DateOnly firstDay = new(year, month, 1);
            return new ExpiryDate(year, month, day: null, firstDay.AddMonths(1));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InventoryValidationException(
                $"O mês de validade não é válido: {exception.ParamName}.");
        }
    }

    public static ExpiryDate ForDay(int year, int month, int day)
    {
        try
        {
            DateOnly date = new(year, month, day);
            return new ExpiryDate(year, month, day, date);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InventoryValidationException(
                $"A data de validade não é válida: {exception.ParamName}.");
        }
    }

    public DateTimeOffset GetBlockingInstant(TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        DateTime localMidnight = BlockingDate.ToDateTime(
            TimeOnly.MinValue,
            DateTimeKind.Unspecified);
        DateTime utc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, timeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}

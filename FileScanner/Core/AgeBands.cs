namespace FileScanner.Core;

// Age bands that color the chart by the newest change inside each folder (NewestWriteUtc).
public static class AgeBands
{
    // Limits in months: under 1, 1 to 6, 6 to 12, 12 to 36, over 36.
    static readonly int[] Months = [1, 6, 12, 36];

    public static int Count => Months.Length + 1;

    // 0 is the newest and Count - 1 the oldest. No known date (empty folder) counts as the oldest.
    public static int Of(DateTime newestUtc, DateTime nowUtc)
    {
        if (newestUtc == default) return Months.Length;
        for (int i = 0; i < Months.Length; i++)
            if (newestUtc > nowUtc.AddMonths(-Months[i])) return i;
        return Months.Length;
    }
}

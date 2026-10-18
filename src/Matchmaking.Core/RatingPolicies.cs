namespace Matchmaking.Core;

public static class RatingPolicies
{
    public const int WinLossPoints = 25;
    public const int MaxPersonalBonus = 3;

    public static int WinLoss(MatchRecord m)
    {
        var baseDelta = m.IsWin ? WinLossPoints : -WinLossPoints;
        var bonus = Math.Clamp(m.Kills - m.Deaths, -MaxPersonalBonus, MaxPersonalBonus);
        return baseDelta + bonus;
    }

    public static int PerformanceFirst(MatchRecord m)
    {
        var kda = (double)m.Kills / Math.Max(m.Deaths, 1);
        var kdaPart = Math.Clamp((kda - 1.0) * 20.0, -40.0, 60.0);
        var durationPart = Math.Min(m.MatchDurationMinutes, 30.0) / 30.0 * 10.0;
        return (int)Math.Round(kdaPart + durationPart, MidpointRounding.AwayFromZero);
    }
}

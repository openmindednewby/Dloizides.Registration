namespace Dloizides.Registration.Model;

public sealed record ScreenVerdict(ScreenOutcome Outcome, string? Code = null)
{
    public static ScreenVerdict Pass { get; } = new(ScreenOutcome.Pass);

    public static ScreenVerdict SilentDrop { get; } = new(ScreenOutcome.SilentDrop);

    public static ScreenVerdict Throttle { get; } = new(ScreenOutcome.Throttle);

    public static ScreenVerdict Reject(string code) => new(ScreenOutcome.Reject, code);
}

public enum ScreenOutcome
{
    Pass = 0,
    SilentDrop = 1,
    Throttle = 2,
    Reject = 3,
}

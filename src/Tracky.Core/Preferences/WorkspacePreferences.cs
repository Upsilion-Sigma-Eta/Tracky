namespace Tracky.Core.Preferences;

public sealed record WorkspacePreferences(
    AppThemePreference Theme,
    bool CompactDensity,
    string ShortcutProfile,
    DateTimeOffset UpdatedAtUtc)
{
    public static WorkspacePreferences Default { get; } =
        new(AppThemePreference.Light, CompactDensity: true, "Default", DateTimeOffset.UnixEpoch);
}

namespace Tracky.Core.Preferences;

public sealed record UpdateWorkspacePreferencesInput(
    AppThemePreference Theme,
    bool CompactDensity,
    string ShortcutProfile);

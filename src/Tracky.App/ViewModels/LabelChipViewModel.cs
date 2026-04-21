using System.Globalization;

namespace Tracky.App.ViewModels;

public sealed class LabelChipViewModel
{
    private static readonly string[] Palette =
    [
        "#D73A4A",
        "#0075CA",
        "#A2EEEF",
        "#7057FF",
        "#008672",
        "#E4E669",
        "#D876E3",
        "#0E8A16",
        "#FBCA04",
        "#1D76DB",
        "#B60205",
        "#5319E7",
        "#C5DEF5",
        "#BFDADC",
        "#FEF2C0",
        "#F9D0C4",
        "#C2E0C6",
        "#BFD4F2",
        "#D4C5F9",
        "#FAD8C7",
    ];

    public LabelChipViewModel(string name)
    {
        Name = name;
        Background = PickColor(name);
        Foreground = IsLight(Background) ? "#1F2328" : "#FFFFFF";
        BorderBrush = ShiftToBorder(Background);
    }

    public string Name { get; }

    public string Background { get; }

    public string Foreground { get; }

    public string BorderBrush { get; }

    private static string PickColor(string value)
    {
        var hash = 0;
        foreach (var c in value)
        {
            hash = unchecked((hash * 31) + char.ToLowerInvariant(c));
        }

        var index = Math.Abs(hash) % Palette.Length;
        return Palette[index];
    }

    private static bool IsLight(string hex)
    {
        if (!TryParse(hex, out var r, out var g, out var b))
        {
            return true;
        }

        var luminance = ((0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b))) / 255.0;
        return luminance > 0.55;

        static double Channel(int c) => c;
    }

    private static string ShiftToBorder(string hex)
    {
        if (!TryParse(hex, out var r, out var g, out var b))
        {
            return hex;
        }

        var factor = IsLight(hex) ? 0.80 : 1.0;
        var rr = (int)Math.Clamp(r * factor, 0, 255);
        var gg = (int)Math.Clamp(g * factor, 0, 255);
        var bb = (int)Math.Clamp(b * factor, 0, 255);
        return $"#{rr:X2}{gg:X2}{bb:X2}";
    }

    private static bool TryParse(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#')
        {
            return false;
        }

        return int.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && int.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && int.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }
}

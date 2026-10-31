namespace Sway.Core.Controller;

/// <summary>Unique labels for displays: names shared by several displays get " (1)", " (2)" in layout order.</summary>
public static class DisplayLabeler
{
    /// <summary>Labels for the displays of a layout.</summary>
    public static Dictionary<DisplayId, string> Labels(DisplayLayout layout)
    {
        var totals = layout.Displays.GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<string, int>();
        var result = new Dictionary<DisplayId, string>();
        foreach (var display in layout.Displays)
        {
            if (totals[display.Name] > 1)
            {
                seen[display.Name] = seen.GetValueOrDefault(display.Name) + 1;
                result[display.Id] = $"{display.Name} ({seen[display.Name]})";
            }
            else
            {
                result[display.Id] = display.Name;
            }
        }

        return result;
    }
}

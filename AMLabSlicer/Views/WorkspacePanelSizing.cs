namespace AMLabSlicer.Views;

internal readonly record struct PanelWidths(double Left, double Right, double LeftMinimum, double RightMinimum);

internal static class WorkspacePanelSizing
{
    public static PanelWidths Fit(double total, double left, double right, bool leftOpen, bool rightOpen)
    {
        var budget = Math.Max(0, total - 300 - (leftOpen ? 4 : 0) - (rightOpen ? 4 : 0));
        var leftMin = leftOpen ? 280d : 0;
        var rightMin = rightOpen ? 300d : 0;
        if (leftMin + rightMin > budget)
        {
            var factor = budget / (leftMin + rightMin);
            leftMin *= factor; rightMin *= factor;
        }
        left = leftOpen ? Math.Clamp(left, leftMin, Math.Max(leftMin, Math.Min(640, total * .5))) : 0;
        right = rightOpen ? Math.Clamp(right, rightMin, Math.Max(rightMin, 600)) : 0;
        var excess = left + right - budget;
        if (excess > 0)
        {
            var adjustable = left + right - leftMin - rightMin;
            var factor = adjustable > 0 ? Math.Max(0, (adjustable - excess) / adjustable) : 0;
            left = leftMin + (left - leftMin) * factor;
            right = rightMin + (right - rightMin) * factor;
        }
        return new(left, right, leftMin, rightMin);
    }
}

namespace EdgeLink.Core;

/// <summary>A rectangle in arbitrary continuous units (millimetres for the physical layout).</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height) {
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public enum PhysicalSizeSource {
    /// <summary>The user typed the diagonal in.</summary>
    Manual,

    /// <summary>Reported by the monitor and consistent with its resolution.</summary>
    Edid,

    /// <summary>The monitor reported a size whose proportions contradict its resolution; only the longer side was kept.</summary>
    EdidCorrected,

    /// <summary>Guessed from the Windows scaling factor.</summary>
    Scaling
}

public readonly record struct PhysicalSize(double WidthMm, double HeightMm, PhysicalSizeSource Source) {
    public double DiagonalInches => Math.Sqrt(WidthMm * WidthMm + HeightMm * HeightMm) / 25.4;
}

/// <summary>
/// Works out how big each monitor really is and arranges the monitors at those sizes, so that the
/// schematic shows a small high-resolution tablet as small. Purely cosmetic: links never use it.
/// </summary>
public static class PhysicalLayout {
    /// <summary>Pixel density assumed for a monitor running at 100% scaling.</summary>
    private const double BasePixelsPerInch = 100;

    /// <summary>Monitors closer than this many pixels are treated as equally near when picking neighbours.</summary>
    private const double TouchTolerance = 64;

    public const double MinDiagonalInches = 3;
    public const double MaxDiagonalInches = 200;

    public static PhysicalSize Estimate(MonitorProfile profile, DetectedMonitor detected)
    {
        double width = detected.Fingerprint.Width, height = detected.Fingerprint.Height;

        if (profile.DiagonalInches is { } inches and >= MinDiagonalInches and <= MaxDiagonalInches) {
            var perPixel = inches * 25.4 / Math.Sqrt(width * width + height * height);
            return new PhysicalSize(width * perPixel, height * perPixel, PhysicalSizeSource.Manual);
        }

        double edidWidth = detected.EdidWidthMm, edidHeight = detected.EdidHeightMm;
        if (edidWidth > 0 && edidHeight > 0) {
            // EDID describes the panel in its native orientation
            if (width >= height != edidWidth >= edidHeight) (edidWidth, edidHeight) = (edidHeight, edidWidth);

            var mismatch = edidWidth / edidHeight / (width / height);
            if (mismatch is > 0.9 and < 1.1) return new PhysicalSize(edidWidth, edidHeight, PhysicalSizeSource.Edid);

            var perPixel = Math.Max(edidWidth, edidHeight) / Math.Max(width, height);
            return new PhysicalSize(width * perPixel, height * perPixel, PhysicalSizeSource.EdidCorrected);
        }

        var pixelsPerInch = BasePixelsPerInch * Math.Max(96, detected.Dpi) / 96.0;
        return new PhysicalSize(width / pixelsPerInch * 25.4, height / pixelsPerInch * 25.4, PhysicalSizeSource.Scaling);
    }

    /// <summary>
    /// Arranges monitors at their physical sizes while keeping the neighbourhood of the Windows
    /// arrangement: each monitor is attached to the same side of the neighbour it sits next to.
    /// Returns one rectangle in millimetres per input, in the same order.
    /// </summary>
    public static RectD[] Arrange(IReadOnlyList<(PixelRect Bounds, PhysicalSize Size)> monitors)
    {
        var result = new RectD[monitors.Count];
        if (monitors.Count == 0) return result;

        var placed = new List<int>();
        var first = 0;
        for (var i = 0; i < monitors.Count; i++) {
            if (monitors[i].Bounds is { X: 0, Y: 0 }) first = i; // the primary monitor anchors the rest
        }

        result[first] = new RectD(0, 0, monitors[first].Size.WidthMm, monitors[first].Size.HeightMm);
        placed.Add(first);

        while (placed.Count < monitors.Count) {
            // next: the unplaced monitor closest to an already placed one, preferring the pair
            // that shares the longest stretch of edge
            int next = -1, anchor = -1;
            double bestDistance = double.MaxValue, bestOverlap = double.MinValue;
            for (var i = 0; i < monitors.Count; i++) {
                if (placed.Contains(i)) continue;

                foreach (var p in placed) {
                    var (gapX, gapY) = Gaps(monitors[p].Bounds, monitors[i].Bounds);
                    // arrangements made by hand are rarely pixel-tight; small gaps count as touching
                    var distance = Math.Floor(Math.Sqrt(Math.Pow(Math.Max(0, gapX), 2) + Math.Pow(Math.Max(0, gapY), 2)) / TouchTolerance);
                    var overlap = -Math.Min(gapX, gapY);
                    if (distance < bestDistance || (distance == bestDistance && overlap > bestOverlap)) {
                        (next, anchor, bestDistance, bestOverlap) = (i, p, distance, overlap);
                    }
                }
            }

            result[next] = Attach(monitors[anchor].Bounds, result[anchor], monitors[next].Bounds, monitors[next].Size);
            result[next] = PushClear(result[next], placed.Select(p => result[p]).ToList());
            placed.Add(next);
        }

        return result;
    }

    /// <summary>Signed distances between two rectangles on each axis; negative when their projections overlap.</summary>
    private static (double X, double Y) Gaps(PixelRect a, PixelRect b) => (
        Math.Max(a.X - b.Right, b.X - a.Right),
        Math.Max(a.Y - b.Bottom, b.Y - a.Bottom));

    private static RectD Attach(PixelRect anchorPixels, RectD anchor, PixelRect pixels, PhysicalSize size)
    {
        var (gapX, gapY) = Gaps(anchorPixels, pixels);

        if (gapY >= gapX) {
            var x = Align(anchorPixels.X, anchorPixels.Width, anchor.X, anchor.Width, pixels.X, pixels.Width, size.WidthMm);
            var y = pixels.Y >= anchorPixels.Y ? anchor.Bottom : anchor.Y - size.HeightMm;
            return new RectD(x, y, size.WidthMm, size.HeightMm);
        }
        else {
            var y = Align(anchorPixels.Y, anchorPixels.Height, anchor.Y, anchor.Height, pixels.Y, pixels.Height, size.HeightMm);
            var x = pixels.X >= anchorPixels.X ? anchor.Right : anchor.X - size.WidthMm;
            return new RectD(x, y, size.WidthMm, size.HeightMm);
        }
    }

    /// <summary>
    /// Positions a monitor along the shared axis by keeping whichever alignment was tightest in the
    /// Windows arrangement — start to start, centre to centre or end to end — so monitors whose tops
    /// line up there still line up here.
    /// </summary>
    private static double Align(int anchorPixelStart, int anchorPixelLength, double anchorStart, double anchorLength, int pixelStart, int pixelLength, double length)
    {
        var perPixel = anchorLength / anchorPixelLength;
        double startOffset = pixelStart - anchorPixelStart;
        double endOffset = pixelStart + pixelLength - (anchorPixelStart + anchorPixelLength);
        var centreOffset = (startOffset + endOffset) / 2;

        var smallest = Math.Min(Math.Abs(startOffset), Math.Min(Math.Abs(endOffset), Math.Abs(centreOffset)));
        var start = Math.Abs(startOffset) == smallest ? anchorStart + startOffset * perPixel
            : Math.Abs(centreOffset) == smallest ? anchorStart + anchorLength / 2 + centreOffset * perPixel - length / 2
            : anchorStart + anchorLength + endOffset * perPixel - length;

        // monitors that share a stretch of edge in Windows must still share one after resizing
        var sharedInPixels = pixelStart < anchorPixelStart + anchorPixelLength && anchorPixelStart < pixelStart + pixelLength;
        if (!sharedInPixels) return start;

        var keep = Math.Min(length, anchorLength) / 4;
        return Math.Clamp(start, anchorStart - length + keep, anchorStart + anchorLength - keep);
    }

    /// <summary>
    /// Moves a freshly attached monitor off any monitor it landed on, each time by the shortest
    /// way out, so a monitor that merely grazes a neighbour slides along its anchor instead of
    /// jumping to the far side.
    /// </summary>
    private static RectD PushClear(RectD rect, List<RectD> placed)
    {
        const double epsilon = 0.01;

        for (var pass = 0; pass < placed.Count * 2; pass++) {
            var moved = false;
            foreach (var other in placed) {
                var overlaps = rect.X < other.Right - epsilon && other.X < rect.Right - epsilon
                               && rect.Y < other.Bottom - epsilon && other.Y < rect.Bottom - epsilon;
                if (!overlaps) continue;

                double left = rect.Right - other.X, right = other.Right - rect.X;
                double up = rect.Bottom - other.Y, down = other.Bottom - rect.Y;
                var shortest = Math.Min(Math.Min(left, right), Math.Min(up, down));

                if (shortest == left) rect = rect with { X = other.X - rect.Width };
                else if (shortest == right) rect = rect with { X = other.Right };
                else if (shortest == up) rect = rect with { Y = other.Y - rect.Height };
                else rect = rect with { Y = other.Bottom };
                moved = true;
            }

            if (!moved) break;
        }

        return rect;
    }
}

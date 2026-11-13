namespace EdgeLink.Core;

public enum Edge {
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3
}

/// <summary>A point in physical virtual-desktop pixels.</summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>A rectangle in physical virtual-desktop pixels. Right and Bottom are exclusive.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height) {
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(PixelPoint p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    public PixelPoint Clamp(PixelPoint p) => new(
        Math.Clamp(p.X, X, Right - 1),
        Math.Clamp(p.Y, Y, Bottom - 1));

    /// <summary>Length in pixels of the given edge.</summary>
    public int EdgeLength(Edge edge) => edge.IsVertical() ? Height : Width;

    /// <summary>Distance in pixels from the point to the given edge, measured inwards.</summary>
    public int DistanceFromEdge(Edge edge, PixelPoint p) => edge switch {
        Edge.Left => p.X - X,
        Edge.Right => Right - 1 - p.X,
        Edge.Top => p.Y - Y,
        _ => Bottom - 1 - p.Y
    };
}

public static class EdgeExtensions {
    /// <summary>Left and right edges run vertically; positions along them are Y coordinates.</summary>
    public static bool IsVertical(this Edge edge) => edge is Edge.Left or Edge.Right;

    public static Edge Opposite(this Edge edge) => edge switch {
        Edge.Left => Edge.Right,
        Edge.Right => Edge.Left,
        Edge.Top => Edge.Bottom,
        _ => Edge.Top
    };
}

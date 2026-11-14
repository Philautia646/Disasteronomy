using Godot;
using System;

public partial class Handrail : Area2D
{
    [Export] public Vector2 StartOffset { get; set; } = new Vector2(-50, 0);
    [Export] public Vector2 EndOffset { get; set; } = new Vector2(50, 0);

    public Vector2 GlobalStart => GlobalPosition + StartOffset;
    public Vector2 GlobalEnd => GlobalPosition + EndOffset;

    public override void _Ready()
    {
        AddToGroup("handrail");
        SetPhysicsProcess(false);
    }

    public override void _Draw()
    {
        DrawLine(StartOffset, EndOffset, Colors.Yellow, 6.0f);
        DrawCircle(StartOffset, 4.0f, Colors.Yellow);
        DrawCircle(EndOffset, 4.0f, Colors.Yellow);
    }

    /// <summary>
    /// Projects a world point onto the rail segment and returns the closest point on the segment.
    /// </summary>
    public Vector2 GetClosestGrabPoint(Vector2 worldPoint)
    {
        Vector2 start = GlobalStart;
        Vector2 end = GlobalEnd;
        Vector2 segment = end - start;
        float segmentLengthSq = segment.LengthSquared();
        if (segmentLengthSq == 0f)
            return start;
        float t = worldPoint.DistanceTo(start) / segment.Length();
        t = Mathf.Clamp(Dot((worldPoint - start), segment) / segmentLengthSq, 0f, 1f);
        return start + segment * t;
    }

    /// <summary>
    /// Returns the rail's primary axis: "horizontal" or "vertical".
    /// </summary>
    public string GetAxis()
    {
        return MathF.Abs(EndOffset.X - StartOffset.X) > MathF.Abs(EndOffset.Y - StartOffset.Y)
            ? "horizontal"
            : "vertical";
    }

    private static float Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;
}
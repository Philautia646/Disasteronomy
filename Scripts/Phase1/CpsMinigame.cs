using Godot;
using System;

public partial class CpsMinigame : CanvasLayer
{
    public event Action MinigameCompleted;

    [Export] public float FreezeThreshold = 90f;
    [Export] public float IdealMin = 180f;
    [Export] public float IdealMax = 360f;
    [Export] public float OverheatThreshold = 540f;
    [Export] public float ProgressPerSecondIdeal = 30f;
    [Export] public float ProgressPerSecondSlow = 6f;
    [Export] public float MaxProgress = 100f;
    [Export] public int PinCount = 3;
    [Export] public float PenaltyTime = 2f;
    [Export] public float PenaltyProgressLoss = 30f;

    private ColorRect panel;
    private Label pinLabel;
    private ProgressBar progressBar;
    private ProgressBar temperatureBar;
    private Node2D pointerPivot;
    private Line2D pointer;

    private float previousAngle = 0f;
    private float angularSpeedAccum = 0f;
    private float angleTimeSum = 0f;
    private int angleSampleCount = 0;

    private int currentPin = 1;
    private float currentProgress = 0f;
    private float currentTemperature = 50f;
    private float frozenTime = 0f;
    private float overheatTime = 0f;
    private bool isActive = false;

    public override void _Ready()
    {
        Visible = false;
        BuildUI();
    }

    private void BuildUI()
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;

        panel = new ColorRect
        {
            Size = new Vector2(320, 320),
            Position = new Vector2(viewportSize.X / 2 - 160, viewportSize.Y / 2 - 160),
            Color = new Color(0.08f, 0.08f, 0.12f, 0.95f)
        };
        AddChild(panel);

        pinLabel = new Label
        {
            Text = "Pin 1/3",
            Position = new Vector2(0, -110),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.AddChild(pinLabel);

        progressBar = new ProgressBar
        {
            Size = new Vector2(220, 24),
            Position = new Vector2(-110, 60),
            MinValue = 0,
            MaxValue = MaxProgress,
            Value = 0
        };
        panel.AddChild(progressBar);

        temperatureBar = new ProgressBar
        {
            Size = new Vector2(220, 14),
            Position = new Vector2(-110, 92),
            MinValue = 0,
            MaxValue = 100,
            Value = 50
        };
        panel.AddChild(temperatureBar);

        pointerPivot = new Node2D { Position = Vector2.Zero };
        panel.AddChild(pointerPivot);

        pointer = new Line2D
        {
            Points = new Vector2[] { Vector2.Zero, new Vector2(0, -110) },
            Width = 4f,
            DefaultColor = Colors.Cyan
        };
        pointerPivot.AddChild(pointer);
    }
    private void UpdateGameplay(double delta)
    {
        float dt = (float)delta;
        float speed = angularSpeedAccum;
        angularSpeedAccum = 0f;

        bool inIdeal = speed >= IdealMin && speed <= IdealMax;
        bool isFrozen = speed < FreezeThreshold && speed > 0f;
        bool isOverheated = speed > OverheatThreshold;

        if (inIdeal)
            currentProgress += ProgressPerSecondIdeal * dt;
        else
            currentProgress += ProgressPerSecondSlow * dt;

        currentProgress = Mathf.Clamp(currentProgress, 0f, MaxProgress);

        if (inIdeal)
        {
            currentTemperature = Mathf.Lerp(currentTemperature, 50f, dt * 2f);
            frozenTime = 0f;
            overheatTime = 0f;
        }
        else
        {
            if (isFrozen)
            {
                currentTemperature -= 25f * dt;
                frozenTime += dt;
            }
            if (isOverheated)
            {
                currentTemperature += 35f * dt;
                overheatTime += dt;
            }
        }

        currentTemperature = Mathf.Clamp(currentTemperature, 0f, 100f);

        if ((frozenTime >= PenaltyTime || overheatTime >= PenaltyTime))
        {
            currentProgress -= PenaltyProgressLoss;
            currentProgress = Mathf.Clamp(currentProgress, 0f, MaxProgress);
            frozenTime = 0f;
            overheatTime = 0f;
        }

        if (currentProgress >= MaxProgress)
        {
            currentPin++;
            currentProgress = 0f;
            currentTemperature = 50f;

            if (currentPin > PinCount)
            {
                MinigameCompleted?.Invoke();
                Close();
            }
            else
            {
                UpdatePinLabel();
            }
        }
    }

    private void UpdatePinLabel()
    {
        pinLabel.Text = $"Pin {currentPin}/{PinCount}";
    }

    private void UpdateUIDisplay()
    {
        progressBar.Value = currentProgress;
        temperatureBar.Value = currentTemperature;

        if (currentTemperature < 30f)
            temperatureBar.Modulate = Colors.Blue;
        else if (currentTemperature > 70f)
            temperatureBar.Modulate = Colors.Red;
        else
            temperatureBar.Modulate = Colors.Green;

        pointerPivot.RotationDegrees = previousAngle;
    }

    public void Open()
    {
        isActive = true;
        Visible = true;
        currentPin = 1;
        currentProgress = 0f;
        currentTemperature = 50f;
        frozenTime = 0f;
        overheatTime = 0f;
        UpdatePinLabel();
    }

    public void Close()
    {
        isActive = false;
        Visible = false;
    }

    private void UpdateInput(double delta)
    {
        Vector2 center = new Vector2(GetViewport().GetVisibleRect().Size.X / 2, GetViewport().GetVisibleRect().Size.Y / 2);
        Vector2 mousePos = GetViewport().GetMousePosition();
        Vector2 toMouse = mousePos - center;

        float newAngle = Mathf.RadToDeg(Mathf.Atan2(toMouse.Y, toMouse.X));

        float deltaAngle = newAngle - previousAngle;
        while (deltaAngle > 180f) deltaAngle -= 360f;
        while (deltaAngle < -180f) deltaAngle += 360f;

        if (Mathf.Abs(deltaAngle) > 90f)
        {
            previousAngle = newAngle;
            return;
        }

        angularSpeedAccum += Mathf.Abs(deltaAngle) / (float)delta;
        angleTimeSum += (float)delta;
        angleSampleCount++;

        previousAngle = newAngle;

        if (angleTimeSum >= 0.15f && angleSampleCount > 0)
        {
            angularSpeedAccum /= angleSampleCount;
            angleTimeSum = 0f;
            angleSampleCount = 0;
        }
    }

    public override void _Process(double delta)
    {
        if (!isActive) return;
        UpdateInput(delta);
        UpdateGameplay(delta);
        UpdateUIDisplay();
    }
}
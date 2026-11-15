using Godot;
using System;

public partial class CpsMinigame : CanvasLayer
{
	public event Action MinigameCompleted;
	public event Action MinigameCancelled;

	// Speeds are in degrees per second (mouse angle around the center of the screen).
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
	[Export] public float MinRadiusPx = 25f; // ignore the mouse when it is this close to the center

	private const float PanelW = 360f;
	private const float PanelH = 380f;

	private ColorRect panel;
	private Label pinLabel;
	private Label hintLabel;
	private Label speedLabel;
	private ProgressBar progressBar;
	private ProgressBar temperatureBar;
	private Node2D pointerPivot;
	private Line2D pointer;

	private float previousAngle = 0f;
	private float smoothedSpeed = 0f;

	private int currentPin = 1;
	private float currentProgress = 0f;
	private float currentTemperature = 50f;
	private float frozenTime = 0f;
	private float overheatTime = 0f;
	private bool isActive = false;
	private bool escWasDown = false;

	public bool IsOpen => isActive;

	public override void _Ready()
	{
		Visible = false;
		BuildUI();
	}

	private void BuildUI()
	{
		panel = new ColorRect
		{
			Size = new Vector2(PanelW, PanelH),
			Color = new Color(0.08f, 0.08f, 0.12f, 0.95f)
		};
		AddChild(panel);

		pinLabel = new Label
		{
			Text = "Pin 1/3",
			Position = new Vector2(0, 12),
			Size = new Vector2(PanelW, 24),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		panel.AddChild(pinLabel);

		hintLabel = new Label
		{
			Text = "Spin the mouse around the dial.\nNot too slow (freezes), not too fast (overheats).",
			Position = new Vector2(0, 40),
			Size = new Vector2(PanelW, 44),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		panel.AddChild(hintLabel);

		// Dial ring (radius 110) centered at the middle of the panel area above the bars
		Vector2 dialCenter = new Vector2(PanelW / 2f, 195f);
		var ringPoints = new Vector2[65];
		for (int i = 0; i <= 64; i++)
		{
			float a = i / 64f * Mathf.Tau;
			ringPoints[i] = dialCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 80f;
		}
		var ring = new Line2D { Points = ringPoints, Width = 2f, DefaultColor = new Color(0.4f, 0.4f, 0.5f) };
		panel.AddChild(ring);

		pointerPivot = new Node2D { Position = dialCenter };
		panel.AddChild(pointerPivot);

		pointer = new Line2D
		{
			Points = new Vector2[] { Vector2.Zero, new Vector2(80, 0) }, // points along +X = angle 0
			Width = 4f,
			DefaultColor = Colors.Cyan
		};
		pointerPivot.AddChild(pointer);

		speedLabel = new Label
		{
			Text = "Speed: 0",
			Position = new Vector2(0, 288),
			Size = new Vector2(PanelW, 24),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		panel.AddChild(speedLabel);

		progressBar = new ProgressBar
		{
			Size = new Vector2(260, 22),
			Position = new Vector2(50, 318),
			MinValue = 0,
			MaxValue = MaxProgress,
			Value = 0,
			ShowPercentage = true
		};
		panel.AddChild(progressBar);

		temperatureBar = new ProgressBar
		{
			Size = new Vector2(260, 14),
			Position = new Vector2(50, 348),
			MinValue = 0,
			MaxValue = 100,
			Value = 50,
			ShowPercentage = false
		};
		panel.AddChild(temperatureBar);
	}

	public void Open()
	{
		var viewportSize = GetViewport().GetVisibleRect().Size;
		panel.Position = new Vector2(viewportSize.X / 2f - PanelW / 2f, viewportSize.Y / 2f - PanelH / 2f);

		// The dial center on screen must match the center used to measure the mouse angle.
		// Dial center in panel coordinates is (PanelW/2, 195); we measure from that exact point.
		isActive = true;
		Visible = true;
		currentPin = 1;
		currentProgress = 0f;
		currentTemperature = 50f;
		frozenTime = 0f;
		overheatTime = 0f;
		smoothedSpeed = 0f;
		escWasDown = Input.IsPhysicalKeyPressed(Key.Escape);

		Vector2 toMouse = GetViewport().GetMousePosition() - GetDialCenterScreen();
		previousAngle = Mathf.RadToDeg(Mathf.Atan2(toMouse.Y, toMouse.X));

		UpdatePinLabel();
	}

	public void Close()
	{
		isActive = false;
		Visible = false;
	}

	private Vector2 GetDialCenterScreen() => panel.Position + new Vector2(PanelW / 2f, 195f);

	private void UpdatePinLabel()
	{
		pinLabel.Text = $"Pin {currentPin}/{PinCount}";
	}

	private void UpdateInput(float dt)
	{
		Vector2 toMouse = GetViewport().GetMousePosition() - GetDialCenterScreen();

		float instSpeed = 0f;
		if (toMouse.Length() >= MinRadiusPx && dt > 0f)
		{
			float newAngle = Mathf.RadToDeg(Mathf.Atan2(toMouse.Y, toMouse.X));
			float deltaAngle = newAngle - previousAngle;
			while (deltaAngle > 180f) deltaAngle -= 360f;
			while (deltaAngle < -180f) deltaAngle += 360f;
			previousAngle = newAngle;
			instSpeed = Mathf.Abs(deltaAngle) / dt;
		}

		// Exponential smoothing so single-frame spikes do not flip the zones.
		float alpha = 1f - Mathf.Exp(-dt * 8f);
		smoothedSpeed = Mathf.Lerp(smoothedSpeed, instSpeed, alpha);
	}

	private void UpdateGameplay(float dt)
	{
		float speed = smoothedSpeed;

		bool inIdeal = speed >= IdealMin && speed <= IdealMax;
		bool isFrozen = speed < FreezeThreshold;
		bool isOverheated = speed > OverheatThreshold;

		if (inIdeal)
			currentProgress += ProgressPerSecondIdeal * dt;
		else
			currentProgress += ProgressPerSecondSlow * dt;

		if (inIdeal)
		{
			currentTemperature = Mathf.Lerp(currentTemperature, 50f, dt * 2f);
			frozenTime = 0f;
			overheatTime = 0f;
		}
		else if (isFrozen)
		{
			currentTemperature -= 25f * dt;
			frozenTime += dt;
			overheatTime = 0f;
		}
		else if (isOverheated)
		{
			currentTemperature += 35f * dt;
			overheatTime += dt;
			frozenTime = 0f;
		}
		else
		{
			currentTemperature = Mathf.Lerp(currentTemperature, 50f, dt * 1f);
			frozenTime = 0f;
			overheatTime = 0f;
		}

		currentTemperature = Mathf.Clamp(currentTemperature, 0f, 100f);

		if (frozenTime >= PenaltyTime || overheatTime >= PenaltyTime)
		{
			currentProgress -= PenaltyProgressLoss;
			frozenTime = 0f;
			overheatTime = 0f;
		}

		currentProgress = Mathf.Clamp(currentProgress, 0f, MaxProgress);

		if (currentProgress >= MaxProgress)
		{
			currentPin++;
			currentProgress = 0f;
			currentTemperature = 50f;

			if (currentPin > PinCount)
			{
				Close();
				MinigameCompleted?.Invoke();
			}
			else
			{
				UpdatePinLabel();
			}
		}
	}

	private void UpdateUIDisplay()
	{
		progressBar.Value = currentProgress;
		temperatureBar.Value = currentTemperature;

		string zone;
		Color zoneColor;
		if (smoothedSpeed >= IdealMin && smoothedSpeed <= IdealMax) { zone = "IDEAL"; zoneColor = Colors.Cyan; }
		else if (smoothedSpeed < FreezeThreshold) { zone = "TOO SLOW"; zoneColor = new Color(0.4f, 0.6f, 1f); }
		else if (smoothedSpeed > OverheatThreshold) { zone = "TOO FAST"; zoneColor = Colors.Red; }
		else { zone = "OK"; zoneColor = Colors.White; }

		speedLabel.Text = $"Speed: {smoothedSpeed:F0} deg/s  [{zone}]";
		pointer.DefaultColor = zoneColor;

		if (currentTemperature < 30f)
			temperatureBar.Modulate = Colors.Blue;
		else if (currentTemperature > 70f)
			temperatureBar.Modulate = Colors.Red;
		else
			temperatureBar.Modulate = Colors.Green;

		pointerPivot.RotationDegrees = previousAngle;
	}

	public override void _Process(double delta)
	{
		if (!isActive) return;

		bool esc = Input.IsPhysicalKeyPressed(Key.Escape);
		bool escPressed = esc && !escWasDown;
		escWasDown = esc;
		if (escPressed)
		{
			Close();
			MinigameCancelled?.Invoke();
			return;
		}

		float dt = (float)delta;
		UpdateInput(dt);
		UpdateGameplay(dt);
		if (isActive) UpdateUIDisplay();
	}
}
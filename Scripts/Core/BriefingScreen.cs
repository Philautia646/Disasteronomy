using Godot;

/// <summary>
/// Mission briefing shown at the start of a phase. Pauses the game until the player presses Start.
/// Usage: BriefingScreen.Open(this, "TITLE", "story text", "controls text");
/// </summary>
public partial class BriefingScreen : CanvasLayer
{
	private Button startButton;

	public static BriefingScreen Open(Node parent, string title, string body, string controls)
	{
		var screen = new BriefingScreen { Layer = 120, ProcessMode = ProcessModeEnum.Always };
		screen.Build(title, body, controls);
		parent.AddChild(screen);
		parent.GetTree().Paused = true;
		return screen;
	}

	private void Build(string title, string body, string controls)
	{
		var overlay = new ColorRect { Color = new Color(0f, 0f, 0f, 0.88f) };
		AddChild(overlay);
		overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var center = new CenterContainer();
		overlay.AddChild(center);
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
		center.AddChild(box);

		var titleLabel = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
		titleLabel.AddThemeFontSizeOverride("font_size", 28);
		box.AddChild(titleLabel);

		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

		box.AddChild(new Label
		{
			Text = body,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center
		});

		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

		box.AddChild(new Label
		{
			Text = controls,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(0.7f, 0.9f, 1f)
		});

		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

		startButton = new Button { Text = "Start mission" };
		startButton.Pressed += Dismiss;
		box.AddChild(startButton);
	}

	public override void _Ready()
	{
		startButton?.GrabFocus();
	}

	private void Dismiss()
	{
		GetTree().Paused = false;
		QueueFree();
	}
}
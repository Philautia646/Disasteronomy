using Godot;

/// <summary>
/// Pause menu toggled with the P key (Esc is already used by the CPS minigame and the terminal).
/// Usage: PauseMenu.Attach(this, "controls text");
/// </summary>
public partial class PauseMenu : CanvasLayer
{
	public string ControlsText = "";
	public string MenuScenePath = "res://Scenes/MainMenu.tscn";

	private Control panel;

	public static PauseMenu Attach(Node parent, string controlsText)
	{
		var menu = new PauseMenu { Layer = 115, ProcessMode = ProcessModeEnum.Always, ControlsText = controlsText };
		parent.AddChild(menu);
		return menu;
	}

	public override void _Ready()
	{
		var overlay = new ColorRect { Color = new Color(0f, 0f, 0f, 0.75f), Visible = false };
		AddChild(overlay);
		overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		panel = overlay;

		var center = new CenterContainer();
		overlay.AddChild(center);
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
		center.AddChild(box);

		var title = new Label { Text = "PAUSED", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 28);
		box.AddChild(title);

		box.AddChild(new Label
		{
			Text = ControlsText,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(0.7f, 0.9f, 1f)
		});

		var resume = new Button { Text = "Resume (P)" };
		resume.Pressed += () => SetPaused(false);
		box.AddChild(resume);

		var restart = new Button { Text = "Restart phase" };
		restart.Pressed += () =>
		{
			GetTree().Paused = false;
			GetTree().ReloadCurrentScene();
		};
		box.AddChild(restart);

		var menu = new Button { Text = "Back to Menu" };
		menu.Pressed += () =>
		{
			GetTree().Paused = false;
			GetTree().ChangeSceneToFile(MenuScenePath);
		};
		box.AddChild(menu);
	}

	public override void _Input(InputEvent @event)
	{
		if (!(@event is InputEventKey k) || !k.Pressed || k.Echo || k.Keycode != Key.P) return;

		// If something else (e.g. the briefing) paused the game, do not stack the pause menu on top.
		if (GetTree().Paused && !panel.Visible) return;

		SetPaused(!panel.Visible);
		GetViewport().SetInputAsHandled();
	}

	private void SetPaused(bool paused)
	{
		panel.Visible = paused;
		GetTree().Paused = paused;
	}
}
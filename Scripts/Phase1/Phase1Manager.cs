using Godot;
using System;
using System.Collections.Generic;

public partial class Phase1Manager : Node2D
{
	public enum PhaseState { Airlock, Transit, CpsArrival, Minigame, Completed, Failed }

	[Export] public Rect2 LevelBounds = new Rect2(-600, -450, 2200, 1300);
	[Export] public Vector2 AirlockPosition = new Vector2(-400, 0);
	[Export] public Vector2 CpsPosition = new Vector2(1400, 0);
	[Export] public float MissionTime = 0f;

	private PhaseState state = PhaseState.Airlock;
	private Dictionary<int, bool> playerCompleted = new();
	private Dictionary<int, bool> playerAtCps = new();
	private string failureReason = "";

	private Area2D cpsTrigger;
	private Node2D tetherAnchor;
	private CpsMinigame cpsMinigame;
	private MultiplayerSpawner spawner;

	private Label hudCharges;
	private Label hudTether;
	private Label hudObjective;
	private Label hudTimer;

	private ColorRect screenOverlay;
	private Label screenTitle;
	private Label screenReason;
	private Label screenDetails;
	private Button screenBtnRetry;
	private Button screenBtnMenu;

	private CanvasLayer worldLayer;
	private CanvasLayer hudLayer;
	private Node2D worldBatch;
	private Node2D starBatch;
	private EvaPlayer localPlayer;

	private Label hudDebug;

	public override void _Ready()
	{
		RegisterGrabInput();
		BuildLevel();
		ConfigureSpawner();
		SetupHud();
		SetupScreens();

		var cps = GetNodeOrNull<Area2D>("CpsTrigger");
		if (cps != null)
		{
			cps.BodyEntered += (body) => { if (body is EvaPlayer p) p.SetAtCps(true); };
			cps.BodyExited += (body) => { if (body is EvaPlayer p) p.SetAtCps(false); };
		}

		if (!Multiplayer.HasMultiplayerPeer())
			SpawnSoloPlayer();

		UpdateObjective();
		GD.Print("Phase1: level built");
	}

	public override void _Draw()
	{
		if (starBatch == null) return;
		var rng = new RandomNumberGenerator { Seed = 12345 };
		for (int i = 0; i < 200; i++)
		{
			var pos = new Vector2(rng.RandfRange(-1200, 1200), rng.RandfRange(-900, 900));
			DrawCircle(pos, 1.5f, Colors.White);
		}
	}

	public override void _Process(double delta)
	{
		if (state == PhaseState.Completed || state == PhaseState.Failed) return;

		MissionTime += (float)delta;
		hudTimer.Text = $"Time: {MissionTime:F1}s";

		UpdateTetherHud();
		UpdateDebugHud();
		UpdateCameraFollow();
	}

	private void RegisterGrabInput()
	{
		if (!InputMap.HasAction("grab"))
		{
			InputMap.AddAction("grab");
			InputMap.ActionAddEvent("grab", new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
			InputMap.ActionAddEvent("grab", new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
		}
	}

	private void UpdateDebugHud()
	{
		if (hudDebug == null || localPlayer == null) return;
		hudDebug.Text = $"Pos: {localPlayer.GlobalPosition:F0} | Vel: {localPlayer.Velocity:F0} | State: {localPlayer.state}";
	}

	private void UpdateCameraFollow()
	{
		if (localPlayer == null || localPlayer.playerCamera == null) return;
		var targetPos = localPlayer.GlobalPosition;
		var camPos = localPlayer.playerCamera.GlobalPosition;
		var smoothed = camPos.Lerp(targetPos, 0.15f);
		localPlayer.playerCamera.GlobalPosition = smoothed;
	}

	private void ConfigureSpawner()
	{
		spawner = GetNodeOrNull<MultiplayerSpawner>("MultiplayerSpawner");
		if (spawner == null)
		{
			spawner = new MultiplayerSpawner { Name = "MultiplayerSpawner" };
			AddChild(spawner);
		}
		spawner.SpawnPath = new NodePath(".");
		spawner.AddSpawnableScene("res://Scenes/Phase1/EvaPlayer.tscn");
		GD.Print("Phase1: spawner configured with EvaPlayer scene");
		
	}

	private void SetupHud()
	{
		hudLayer = new CanvasLayer { Name = "HUD", Layer = 100 };
		AddChild(hudLayer);

		hudCharges = new Label { Position = new Vector2(20, 20), Text = "SAFER: 3/3" };
		hudLayer.AddChild(hudCharges);

		hudTether = new Label { Position = new Vector2(20, 44), Text = "Tether: OK" };
		hudLayer.AddChild(hudTether);

		hudObjective = new Label { Position = new Vector2(20, 68), Text = "Objective: Reach the CPS" };
		hudLayer.AddChild(hudObjective);

		hudTimer = new Label { Position = new Vector2(20, 92), Text = "Time: 0.0s" };
		hudLayer.AddChild(hudTimer);

		hudDebug = new Label { Position = new Vector2(20, 116), Text = "Debug: waiting..." };
		hudLayer.AddChild(hudDebug);
	}

	private void SpawnSoloPlayer()
	{
		var player = new EvaPlayer();
		player.Name = "1";
		player.Position = AirlockPosition + new Vector2(0, -30);
		AddChild(player);
		localPlayer = player;
		player.SetMultiplayerAuthority(1, true);
		GD.Print($"Phase1: player spawned at {localPlayer.GlobalPosition}, in tree = true");
	}

	private void BuildLevel()
	{
		// World canvas layer (draws behind everything)
		worldLayer = new CanvasLayer { Name = "WorldLayer", Layer = -100 };
		AddChild(worldLayer);

		// Dark space background
		var bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.06f, 1f) };
		bg.Size = GetViewport().GetVisibleRect().Size;
		worldLayer.AddChild(bg);

		// Star batch node (stars drawn via _Draw)
		starBatch = new Node2D { Name = "Stars" };
		worldLayer.AddChild(starBatch);

		// Level bounds rect (visual only)
		var bounds = new ColorRect { Color = new Color(0.3f, 0.3f, 0.4f, 0.15f) };
		bounds.Size = new Vector2(LevelBounds.Size.X, LevelBounds.Size.Y);
		bounds.Position = new Vector2(LevelBounds.Position.X, LevelBounds.Position.Y);
		worldLayer.AddChild(bounds);

		// Airlock block
		var airlock = new ColorRect { Size = new Vector2(100, 140), Color = new Color(0.5f, 0.5f, 0.55f) };
		airlock.Position = AirlockPosition + new Vector2(-50, -70);
		worldLayer.AddChild(airlock);
		var airlockLabel = new Label
		{
			Text = "Utility Tunnel / MMSEV",
			Position = AirlockPosition + new Vector2(-90, -100),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		worldLayer.AddChild(airlockLabel);

		// CPS block
		var cpsBlock = new ColorRect { Size = new Vector2(120, 160), Color = new Color(0.9f, 0.8f, 0.2f) };
		cpsBlock.Position = CpsPosition + new Vector2(-60, -80);
		worldLayer.AddChild(cpsBlock);
		var cpsLabel = new Label
		{
			Text = "Cryogenic Propulsion Stage",
			Position = CpsPosition + new Vector2(-120, -110),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		worldLayer.AddChild(cpsLabel);
		var frozenMarker = new ColorRect { Size = new Vector2(20, 20), Color = Colors.Red };
		frozenMarker.Position = CpsPosition + new Vector2(40, 40);
		worldLayer.AddChild(frozenMarker);

		// Tether anchor at airlock
		tetherAnchor = new Node2D { Name = "TetherAnchor", Position = AirlockPosition };
		AddChild(tetherAnchor);
		tetherAnchor.AddToGroup("tether_anchor");

		// CPS trigger area
		cpsTrigger = new Area2D { Name = "CpsTrigger" };
		cpsTrigger.Position = CpsPosition;
		var cpsShape = new CollisionShape2D();
		cpsShape.Shape = new RectangleShape2D { Size = new Vector2(100, 100) };
		cpsTrigger.AddChild(cpsShape);
		AddChild(cpsTrigger);
		cpsTrigger.AddToGroup("cps_trigger");

		// Handrail path (8 rails: horizontal and vertical, with gaps)
		CreateHandrail(new Vector2(-300, 0), new Vector2(-100, 0));
		CreateHandrail(new Vector2(-100, 0), new Vector2(-100, -120));
		CreateHandrail(new Vector2(-100, -120), new Vector2(100, -120));
		CreateHandrail(new Vector2(100, -120), new Vector2(100, 0));
		CreateHandrail(new Vector2(100, 0), new Vector2(300, 0));
		CreateHandrail(new Vector2(300, 0), new Vector2(300, 120));
		CreateHandrail(new Vector2(300, 120), new Vector2(1300, 120));
		CreateHandrail(new Vector2(1300, 120), new Vector2(1300, 0));
	}

	private void CreateHandrail(Vector2 start, Vector2 end)
	{
		var rail = new Handrail { Name = $"Handrail_{start}_{end}" };
		rail.Position = start;
		rail.StartOffset = Vector2.Zero;
		rail.EndOffset = end - start;
		worldLayer.AddChild(rail);
	}

	private void SetupScreens()
	{
		screenOverlay = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.8f),
			Size = GetViewport().GetVisibleRect().Size,
			Visible = false
		};
		AddChild(screenOverlay);

		var screenContainer = new VBoxContainer
		{
			Position = new Vector2(GetViewport().GetVisibleRect().Size.X / 2 - 150, GetViewport().GetVisibleRect().Size.Y / 2 - 150),
			Size = new Vector2(300, 200),
			Alignment = BoxContainer.AlignmentMode.Center
		};
		screenOverlay.AddChild(screenContainer);

		screenTitle = new Label { Text = "MISSION FAILED", HorizontalAlignment = HorizontalAlignment.Center };
		screenContainer.AddChild(screenTitle);

		screenReason = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		screenContainer.AddChild(screenReason);

		screenDetails = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		screenContainer.AddChild(screenDetails);

		screenBtnRetry = new Button { Text = "Retry" };
		screenBtnRetry.Pressed += Retry;
		screenContainer.AddChild(screenBtnRetry);

		screenBtnMenu = new Button { Text = "Back to Menu" };
		screenBtnMenu.Pressed += BackToMenu;
		screenContainer.AddChild(screenBtnMenu);
	}
	public void RequestMinigame()
	{
		if (state == PhaseState.Minigame) return;
		if (cpsMinigame == null)
		{
			cpsMinigame = new CpsMinigame();
			cpsMinigame.MinigameCompleted += OnLocalMinigameCompleted;
			AddChild(cpsMinigame);
		}
		cpsMinigame.Open();
		state = PhaseState.Minigame;
		UpdateObjective();
	}

	private void OnLocalMinigameCompleted()
	{
		int peerId = Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 1;
		playerCompleted[peerId] = true;
		RpcReportComplete(peerId);
		CheckCompletion();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void RpcReportComplete(int peerId)
	{
		playerCompleted[peerId] = true;
		CheckCompletion();
	}

	private void CheckCompletion()
	{
		int required = Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetPeers().Length + 1 : 1;
		int completed = 0;
		foreach (var kv in playerCompleted)
			if (kv.Value) completed++;

		if (completed >= required && state != PhaseState.Completed)
		{
			state = PhaseState.Completed;
			ShowSuccessScreen();
		}
	}

	public void OnMissionFailed(string reason)
	{
		if (state == PhaseState.Completed || state == PhaseState.Failed) return;
		failureReason = reason;
		state = PhaseState.Failed;
		RpcReportFailed(reason);
		ShowFailureScreen();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void RpcReportFailed(string reason)
	{
		failureReason = reason;
		state = PhaseState.Failed;
		ShowFailureScreen();
	}

	private void UpdateTetherHud()
	{
		foreach (Node node in GetTree().GetNodesInGroup("network_players"))
		{
			if (!(node is EvaPlayer p)) continue;
			if (!p.IsMultiplayerAuthority()) continue;

			hudCharges.Text = $"SAFER: {p.GetSaferCharges()}/3";
			break;
		}
	}

	private void UpdateObjective()
	{
		string objective = state switch
		{
			PhaseState.Airlock => "Objective: Exit the airlock and grab the handrails",
			PhaseState.Transit => "Objective: Move along handrails to the CPS",
			PhaseState.CpsArrival => "Objective: Press E at the CPS to start calibration",
			PhaseState.Minigame => "Objective: Complete the CPS calibration minigame",
			_ => "Objective: Mission complete"
		};
		hudObjective.Text = objective;
	}

	private void ShowSuccessScreen()
	{
		screenOverlay.Visible = true;
		screenTitle.Text = "PHASE 1 COMPLETE";
		screenReason.Text = "Thermal actuators unfrozen.";
		screenDetails.Text = $"Time: {MissionTime:F1}s | Energy: 160 kWh | Comms: stable (1.3-2s latency) | Mass: ~115,000 kg";
		screenBtnRetry.Visible = false;
		screenBtnMenu.Text = "Back to Menu";
		screenBtnMenu.Visible = true;
	}

	private void ShowFailureScreen()
	{
		screenOverlay.Visible = true;
		screenTitle.Text = "MISSION FAILED";
		screenReason.Text = failureReason;
		screenDetails.Text = "Return to the airlock and try again.";
		screenBtnRetry.Visible = true;
		screenBtnMenu.Text = "Back to Menu";
		screenBtnMenu.Visible = true;
	}

	private void Retry()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Phase1/Phase1.tscn");
	}

	private void BackToMenu()
	{
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}
}

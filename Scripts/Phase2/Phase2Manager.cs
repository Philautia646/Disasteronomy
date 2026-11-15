using Godot;
using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Phase 2 - "Efeito Cascata": ammonia leak in the ISS Lab (ECLSS).
/// Close 3 manual valves, replace carbon filters to keep oxygen up, and restart the
/// life-support software at the upper terminal using the (garbled, delayed) code from Houston.
/// The whole level is built in code; the .tscn is just a Node2D with this script.
/// </summary>
public partial class Phase2Manager : Node2D
{
	public enum PhaseState { Playing, Completed, Failed }

	private enum InteractKind { Valve, Filter, Terminal }

	private class Interactable
	{
		public InteractKind Kind;
		public string Name;
		public Vector2 Pos;
		public float Radius = 60f;
		public float HoldTime = 0f;
		public float Progress = 0f;
		public bool Done = false;
		public ColorRect Visual;
	}

	[Export] public Rect2 LevelBounds = new Rect2(-100, -100, 2700, 1060);
	[Export] public Vector2 SpawnPosition = new Vector2(120, 880);
	[Export] public bool DebugKeys = true; // F9 = teleport to the next interactable

	[Export] public float OxygenMax = 100f;
	[Export] public float OxygenDrainClean = 0.6f;   // per second, clean air
	[Export] public float OxygenDrainMist = 1.6f;    // per second, in toxic mist (full leak)
	[Export] public float FilterOxygenBonus = 30f;

	[Export] public float SanityStart = 75f;
	[Export] public float SanityFloor = 40f;
	[Export] public float SanityDecayBase = 0.12f;   // per second
	[Export] public float SanityDecayMist = 0.30f;   // extra per second in mist (full leak)

	[Export] public float ValveHoldTime = 2f;
	[Export] public float FilterHoldTime = 3f;
	[Export] public float TerminalResetTime = 3f;

	[Export] public float RadioLatency = 7f;         // seconds until the first transmission arrives
	[Export] public float RadioInterval = 12f;
	[Export] public float VisionRadius = 0.2f;       // in mist, relative to screen height

	private const int ValveCount = 3;
	private const string FogShaderCode = @"
shader_type canvas_item;
uniform float strength = 0.0;
uniform float dread = 0.0;
uniform float radius = 0.2;
uniform float aspect = 1.78;
uniform vec2 center = vec2(0.5, 0.5);
uniform vec4 tint : source_color = vec4(0.05, 0.28, 0.1, 1.0);
void fragment() {
	vec2 p = (UV - center) * vec2(aspect, 1.0);
	float d = length(p);
	float mist = smoothstep(radius, radius + 0.3, d) * strength * 0.93;
	mist = clamp(mist + strength * 0.12, 0.0, 1.0);
	float edge = smoothstep(0.35, 0.9, length((UV - vec2(0.5)) * vec2(aspect, 1.0))) * dread * 0.55;
	float a = clamp(mist + edge, 0.0, 1.0);
	vec3 col = mix(vec3(0.5, 0.0, 0.0), tint.rgb, mist / max(mist + edge, 0.0001));
	COLOR = vec4(col, a);
}
";

	private PhaseState state = PhaseState.Playing;
	private readonly RandomNumberGenerator rng = new RandomNumberGenerator();

	private Phase2Player player;
	private Node2D worldLayer;
	private ColorRect mistVisual;
	private ShaderMaterial fogMaterial;
	private Rect2 mistRect = new Rect2(-100, 520, 2700, 440);

	private readonly List<Rect2> ladders = new();
	private readonly List<Rect2> wetRects = new();
	private readonly List<Interactable> interactables = new();
	private Interactable terminalInteractable;

	private int valvesClosed = 0;
	private int filtersUsed = 0;
	private float oxygen;
	private float sanity;
	private float missionTime = 0f;
	private float mistAmount = 0f;
	private bool eWasDown = false;
	private bool f9WasDown = false;
	private int debugTeleportIndex = 0;

	// Radio / terminal
	private string resetCode = "0000";
	private float nextRadioAt;
	private readonly List<string> radioLog = new();
	private bool terminalOpen = false;
	private bool terminalResetting = false;
	private bool terminalDone = false;
	private float terminalResetProgress = 0f;
	private float terminalCloseTimer = 0f;
	private string terminalInput = "";

	// HUD
	private CanvasLayer hudLayer;
	private ProgressBar oxygenBar;
	private ProgressBar sanityBar;
	private Label hudStatus;
	private Label hudObjective;
	private Label hudTimer;
	private Label hudDebug;
	private Label hudPrompt;
	private Label hudRadio;
	private Label hudToast;
	private float toastTimer = 0f;

	// Terminal UI
	private CanvasLayer terminalLayer;
	private Label terminalCodeLabel;
	private Label terminalStatusLabel;
	private ProgressBar terminalBar;

	// End screens
	private ColorRect screenOverlay;
	private Label screenTitle;
	private Label screenReason;
	private Label screenDetails;
	private Button screenBtnRetry;
	private Button screenBtnMenu;

	// Called by Phase2Player
	public bool IsOnLadder(Vector2 pos, out float ladderX)
	{
		foreach (var l in ladders)
		{
			if (l.HasPoint(pos))
			{
				ladderX = l.Position.X + l.Size.X / 2f;
				return true;
			}
		}
		ladderX = pos.X;
		return false;
	}

	public bool IsWet(Vector2 feetPos)
	{
		foreach (var w in wetRects)
			if (w.HasPoint(feetPos)) return true;
		return false;
	}

	public override void _Ready()
	{
		rng.Randomize();
		var sb = new StringBuilder();
		for (int i = 0; i < 4; i++) sb.Append(rng.RandiRange(0, 9));
		resetCode = sb.ToString();

		oxygen = OxygenMax;
		sanity = SanityStart;
		nextRadioAt = RadioLatency;

		BuildLevel();
		SpawnPlayer();
		SetupFog();
		SetupHud();
		SetupTerminal();
		SetupScreens();
		UpdateObjective();
		GD.Print($"Phase2: level built (debug: reset code = {resetCode})");
	}

	// ------------------------------------------------------------------ LEVEL

	private void BuildLevel()
	{
		var backgroundLayer = new CanvasLayer { Name = "Background", Layer = -100 };
		AddChild(backgroundLayer);
		var bg = new ColorRect { Color = new Color(0.05f, 0.07f, 0.12f) };
		backgroundLayer.AddChild(bg);
		bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		worldLayer = new Node2D { Name = "World", ZIndex = -5 };
		AddChild(worldLayer);

		// Experiment racks (decorative tall bars)
		float[] rackX = { 420f, 900f, 1250f, 1800f, 2150f };
		foreach (float x in rackX)
		{
			var rack = new ColorRect { Color = new Color(0.55f, 0.6f, 0.7f, 0.12f), Position = new Vector2(x, -60f), Size = new Vector2(90f, 960f) };
			rack.MouseFilter = Control.MouseFilterEnum.Ignore;
			worldLayer.AddChild(rack);
		}

		// Solid: floor and walls (layer 1)
		AddPlatform(new Rect2(-100f, 900f, 2700f, 60f), false, false, new Color(0.22f, 0.24f, 0.3f));
		AddPlatform(new Rect2(-140f, -100f, 40f, 1060f), false, false, new Color(0.22f, 0.24f, 0.3f));
		AddPlatform(new Rect2(2600f, -100f, 40f, 1060f), false, false, new Color(0.22f, 0.24f, 0.3f));

		// One-way platforms (layer 2). Mid level y=640, upper level y=380.
		Color metal = new Color(0.4f, 0.43f, 0.5f);
		Color wetMetal = new Color(0.3f, 0.5f, 0.65f);
		AddPlatform(new Rect2(150f, 640f, 700f, 16f), true, false, metal);
		AddPlatform(new Rect2(930f, 640f, 820f, 16f), true, true, wetMetal);
		AddPlatform(new Rect2(1830f, 640f, 720f, 16f), true, false, metal);
		AddPlatform(new Rect2(100f, 380f, 680f, 16f), true, false, metal);
		AddPlatform(new Rect2(860f, 380f, 660f, 16f), true, true, wetMetal);
		AddPlatform(new Rect2(1600f, 380f, 950f, 16f), true, false, metal);

		// Wet puddle on the floor
		var puddle = new ColorRect { Color = new Color(0.3f, 0.5f, 0.65f, 0.8f), Position = new Vector2(1900f, 896f), Size = new Vector2(550f, 6f) };
		puddle.MouseFilter = Control.MouseFilterEnum.Ignore;
		worldLayer.AddChild(puddle);
		wetRects.Add(new Rect2(1900f, 892f, 550f, 12f));

		// Racks (ladders): ground <-> mid, mid <-> upper
		AddLadder(300f, 620f, 900f);
		AddLadder(1300f, 620f, 900f);
		AddLadder(2100f, 620f, 900f);
		AddLadder(700f, 360f, 640f);
		AddLadder(1450f, 360f, 640f);
		AddLadder(2400f, 360f, 640f);

		// Level labels
		AddWorldLabel("UPPER LEVEL (Control)", new Vector2(110f, 330f));
		AddWorldLabel("MID LEVEL (Racks)", new Vector2(160f, 590f));
		AddWorldLabel("LOWER LEVEL (Subfloor)", new Vector2(10f, 850f));

		// MMOD impact marker (the cause of the leak)
		var hit = new ColorRect { Color = new Color(0.9f, 0.15f, 0.15f), Position = new Vector2(2460f, 110f), Size = new Vector2(28f, 28f) };
		worldLayer.AddChild(hit);
		AddWorldLabel("MMOD impact", new Vector2(2420f, 80f));

		// Interactables: 3 valves, 3 carbon filters, 1 terminal
		AddInteractable(InteractKind.Valve, "Valve A", new Vector2(1000f, 878f), ValveHoldTime, new Vector2(24f, 24f), new Color(1f, 0.55f, 0.1f));
		AddInteractable(InteractKind.Valve, "Valve B", new Vector2(2200f, 618f), ValveHoldTime, new Vector2(24f, 24f), new Color(1f, 0.55f, 0.1f));
		AddInteractable(InteractKind.Valve, "Valve C", new Vector2(2300f, 358f), ValveHoldTime, new Vector2(24f, 24f), new Color(1f, 0.55f, 0.1f));
		AddInteractable(InteractKind.Filter, "Carbon filter", new Vector2(500f, 875f), FilterHoldTime, new Vector2(22f, 40f), new Color(0.6f, 0.6f, 0.62f));
		AddInteractable(InteractKind.Filter, "Carbon filter", new Vector2(500f, 615f), FilterHoldTime, new Vector2(22f, 40f), new Color(0.6f, 0.6f, 0.62f));
		AddInteractable(InteractKind.Filter, "Carbon filter", new Vector2(1500f, 615f), FilterHoldTime, new Vector2(22f, 40f), new Color(0.6f, 0.6f, 0.62f));
		terminalInteractable = AddInteractable(InteractKind.Terminal, "Reset terminal", new Vector2(300f, 358f), 0f, new Vector2(36f, 36f), new Color(0.2f, 0.5f, 0.9f));

		// Toxic mist overlay in world space (above the player, fades as valves are closed)
		mistVisual = new ColorRect
		{
			Color = new Color(0.15f, 0.7f, 0.25f, 0.22f),
			Position = mistRect.Position,
			Size = mistRect.Size,
			ZIndex = 5,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		AddChild(mistVisual);
	}

	private void AddPlatform(Rect2 r, bool oneWay, bool wet, Color color)
	{
		var body = new StaticBody2D
		{
			Position = r.Position,
			CollisionLayer = oneWay ? 2u : 1u,
			CollisionMask = 0u
		};
		var shape = new CollisionShape2D
		{
			Shape = new RectangleShape2D { Size = r.Size },
			Position = r.Size / 2f,
			OneWayCollision = oneWay
		};
		body.AddChild(shape);
		var visual = new ColorRect { Color = color, Size = r.Size };
		visual.MouseFilter = Control.MouseFilterEnum.Ignore;
		body.AddChild(visual);
		worldLayer.AddChild(body);

		if (wet)
			wetRects.Add(new Rect2(r.Position.X, r.Position.Y - 8f, r.Size.X, 12f));
	}

	private void AddLadder(float x, float top, float bottom)
	{
		ladders.Add(new Rect2(x - 22f, top, 44f, bottom - top));

		Color rail = new Color(0.65f, 0.7f, 0.8f, 0.9f);
		var left = new ColorRect { Color = rail, Position = new Vector2(x - 16f, top), Size = new Vector2(4f, bottom - top) };
		var right = new ColorRect { Color = rail, Position = new Vector2(x + 12f, top), Size = new Vector2(4f, bottom - top) };
		left.MouseFilter = Control.MouseFilterEnum.Ignore;
		right.MouseFilter = Control.MouseFilterEnum.Ignore;
		worldLayer.AddChild(left);
		worldLayer.AddChild(right);
		for (float y = top + 10f; y < bottom; y += 24f)
		{
			var rung = new ColorRect { Color = rail, Position = new Vector2(x - 14f, y), Size = new Vector2(28f, 3f) };
			rung.MouseFilter = Control.MouseFilterEnum.Ignore;
			worldLayer.AddChild(rung);
		}
	}

	private void AddWorldLabel(string text, Vector2 pos)
	{
		var label = new Label { Text = text, Position = pos };
		label.Modulate = new Color(1f, 1f, 1f, 0.6f);
		worldLayer.AddChild(label);
	}

	private Interactable AddInteractable(InteractKind kind, string name, Vector2 pos, float holdTime, Vector2 size, Color color)
	{
		var it = new Interactable { Kind = kind, Name = name, Pos = pos, HoldTime = holdTime };
		it.Visual = new ColorRect { Color = color, Size = size, Position = pos - size / 2f };
		it.Visual.MouseFilter = Control.MouseFilterEnum.Ignore;
		worldLayer.AddChild(it.Visual);

		var tag = new Label { Text = name, Position = new Vector2(-40f, -size.Y / 2f - 24f), Size = new Vector2(120f, 20f) };
		tag.Modulate = new Color(1f, 1f, 1f, 0.8f);
		it.Visual.AddChild(tag);
		tag.Position = new Vector2(size.X / 2f - 60f, -24f);
		tag.Size = new Vector2(120f, 20f);
		tag.HorizontalAlignment = HorizontalAlignment.Center;

		interactables.Add(it);
		return it;
	}

	private void SpawnPlayer()
	{
		player = new Phase2Player { Name = "Player" };
		player.Position = SpawnPosition;
		AddChild(player);
	}

	// ------------------------------------------------------------------ FOG

	private void SetupFog()
	{
		var fogLayer = new CanvasLayer { Name = "Fog", Layer = 20 };
		AddChild(fogLayer);

		var shader = new Shader { Code = FogShaderCode };
		fogMaterial = new ShaderMaterial { Shader = shader };
		var rect = new ColorRect { Material = fogMaterial, MouseFilter = Control.MouseFilterEnum.Ignore };
		fogLayer.AddChild(rect);
		rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		fogMaterial.SetShaderParameter("radius", VisionRadius);
	}

	private void UpdateFog(float dt, bool inMist, float leak)
	{
		if (fogMaterial == null || player == null) return;

		float target = inMist ? leak : 0f;
		mistAmount = Mathf.MoveToward(mistAmount, target, dt * 1.5f);

		Vector2 vpSize = GetViewport().GetVisibleRect().Size;
		Vector2 screenPos = player.GetGlobalTransformWithCanvas().Origin;
		Vector2 uv = new Vector2(screenPos.X / vpSize.X, screenPos.Y / vpSize.Y);

		fogMaterial.SetShaderParameter("strength", mistAmount);
		fogMaterial.SetShaderParameter("dread", GetDread());
		fogMaterial.SetShaderParameter("aspect", vpSize.X / vpSize.Y);
		fogMaterial.SetShaderParameter("center", uv);

		if (mistVisual != null)
			mistVisual.Color = new Color(0.15f, 0.7f, 0.25f, 0.22f * leak);
	}

	private float GetDread()
	{
		float range = Mathf.Max(0.01f, SanityStart - SanityFloor);
		return Mathf.Clamp((SanityStart - sanity) / range, 0f, 1f);
	}

	// ------------------------------------------------------------------ HUD

	private void SetupHud()
	{
		hudLayer = new CanvasLayer { Name = "HUD", Layer = 100 };
		AddChild(hudLayer);

		hudLayer.AddChild(new Label { Text = "O2", Position = new Vector2(20, 20) });
		oxygenBar = new ProgressBar { Position = new Vector2(60, 20), Size = new Vector2(240, 20), MinValue = 0, MaxValue = OxygenMax, Value = OxygenMax, ShowPercentage = true };
		hudLayer.AddChild(oxygenBar);

		hudLayer.AddChild(new Label { Text = "SAN", Position = new Vector2(20, 48) });
		sanityBar = new ProgressBar { Position = new Vector2(60, 48), Size = new Vector2(240, 20), MinValue = 0, MaxValue = 100, Value = SanityStart, ShowPercentage = true };
		hudLayer.AddChild(sanityBar);

		hudStatus = new Label { Position = new Vector2(20, 76), Text = "" };
		hudLayer.AddChild(hudStatus);

		hudObjective = new Label { Position = new Vector2(20, 100), Text = "" };
		hudLayer.AddChild(hudObjective);

		hudTimer = new Label { Position = new Vector2(20, 124), Text = "Time: 0.0s" };
		hudLayer.AddChild(hudTimer);

		hudDebug = new Label { Position = new Vector2(20, 148), Text = "" };
		hudLayer.AddChild(hudDebug);

		hudPrompt = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		hudLayer.AddChild(hudPrompt);
		hudPrompt.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
		hudPrompt.OffsetTop = -90f;
		hudPrompt.OffsetBottom = -50f;

		hudToast = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		hudLayer.AddChild(hudToast);
		hudToast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		hudToast.OffsetTop = 60f;
		hudToast.OffsetBottom = 90f;

		hudRadio = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart };
		hudLayer.AddChild(hudRadio);
		hudRadio.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
		hudRadio.OffsetLeft = -460f;
		hudRadio.OffsetRight = -20f;
		hudRadio.OffsetTop = 20f;
		hudRadio.OffsetBottom = 180f;
	}

	private void UpdateHud(float dt)
	{
		oxygenBar.Value = oxygen;
		oxygenBar.Modulate = oxygen < 25f ? Colors.Red : (oxygen < 50f ? Colors.Yellow : Colors.White);
		sanityBar.Value = sanity;

		string terminalText = terminalDone ? "done" : "pending";
		hudStatus.Text = $"Valves: {valvesClosed}/{ValveCount} | Filters used: {filtersUsed}/3 | Terminal: {terminalText}";
		hudTimer.Text = $"Time: {missionTime:F1}s";
		if (DebugKeys)
			hudDebug.Text = "F9 = teleport to next object";

		var sb = new StringBuilder();
		sb.Append("RADIO (Houston, ~").Append((int)RadioLatency).Append("s delay)\n");
		if (radioLog.Count == 0)
			sb.Append("...waiting for Houston...");
		else
			sb.Append(string.Join("\n", radioLog));
		hudRadio.Text = sb.ToString();

		if (toastTimer > 0f)
		{
			toastTimer -= dt;
			if (toastTimer <= 0f) hudToast.Text = "";
		}
	}

	private void ShowToast(string text)
	{
		hudToast.Text = text;
		toastTimer = 3f;
	}

	private void UpdateObjective()
	{
		string objective;
		if (state == PhaseState.Completed) objective = "Objective: Life support restored";
		else if (state == PhaseState.Failed) objective = "Objective: Mission failed";
		else if (valvesClosed < ValveCount && !terminalDone) objective = "Objective: Close the 3 valves (hold E), then reset the software at the upper terminal";
		else if (valvesClosed < ValveCount) objective = "Objective: Close the remaining valves (hold E)";
		else objective = "Objective: Reset the life-support software at the upper terminal (E)";
		hudObjective.Text = objective;
	}

	// ------------------------------------------------------------------ MAIN LOOP

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		if (state != PhaseState.Playing) return;

		missionTime += dt;

		HandleDebugKeys();

		bool inMist = player != null && mistRect.HasPoint(player.GlobalPosition);
		float leak = 1f - (float)valvesClosed / ValveCount;

		UpdateStats(dt, inMist, leak);
		if (state != PhaseState.Playing) return;

		UpdateInteraction(dt);
		UpdateRadio();
		UpdateTerminal(dt);
		UpdateFog(dt, inMist, leak);
		UpdateHud(dt);

		if (player != null)
			player.Locked = terminalOpen;
	}

	private void UpdateStats(float dt, bool inMist, float leak)
	{
		float drain = OxygenDrainClean + (inMist ? (OxygenDrainMist - OxygenDrainClean) * leak : 0f);
		oxygen = Mathf.Max(0f, oxygen - drain * dt);

		float sanityDecay = SanityDecayBase + (inMist ? SanityDecayMist * leak : 0f);
		sanity = Mathf.Max(SanityFloor, sanity - sanityDecay * dt);

		if (oxygen <= 0f)
			Fail("Oxygen depleted.");
	}

	private void HandleDebugKeys()
	{
		bool f9 = Input.IsPhysicalKeyPressed(Key.F9);
		bool f9Pressed = f9 && !f9WasDown;
		f9WasDown = f9;
		if (!DebugKeys || !f9Pressed || player == null || interactables.Count == 0 || terminalOpen) return;

		var it = interactables[debugTeleportIndex % interactables.Count];
		debugTeleportIndex++;
		player.GlobalPosition = it.Pos + new Vector2(0f, 6f);
		player.Velocity = Vector2.Zero;
		GD.Print($"[DEBUG] F9: teleported to {it.Name}");
	}

	// ------------------------------------------------------------------ INTERACTION

	private void UpdateInteraction(float dt)
	{
		bool eDown = Input.IsPhysicalKeyPressed(Key.E);
		bool ePressed = eDown && !eWasDown;
		eWasDown = eDown;

		if (player == null) return;

		Interactable target = null;
		float best = float.MaxValue;
		foreach (var it in interactables)
		{
			if (it.Done) continue;
			float d = player.GlobalPosition.DistanceTo(it.Pos);
			if (d <= it.Radius && d < best)
			{
				best = d;
				target = it;
			}
		}

		// Progress of anything you are not working on slowly decays.
		foreach (var it in interactables)
			if (it != target && !it.Done && it.Progress > 0f)
				it.Progress = Mathf.Max(0f, it.Progress - dt * 2f);

		if (terminalOpen || target == null)
		{
			hudPrompt.Text = "";
			return;
		}

		if (target.Kind == InteractKind.Terminal)
		{
			hudPrompt.Text = "Press E to use the terminal";
			if (ePressed) OpenTerminal();
			return;
		}

		int pct = (int)(target.Progress / target.HoldTime * 100f);
		string verb = target.Kind == InteractKind.Valve ? "close the valve" : "replace the carbon filter";
		hudPrompt.Text = eDown ? $"{target.Name}: {pct}%" : $"Hold E to {verb}";

		if (eDown)
		{
			target.Progress += dt;
			if (target.Progress >= target.HoldTime)
				CompleteInteraction(target);
		}
		else
		{
			target.Progress = Mathf.Max(0f, target.Progress - dt * 2f);
		}
	}

	private void CompleteInteraction(Interactable it)
	{
		it.Done = true;
		it.Progress = it.HoldTime;

		if (it.Kind == InteractKind.Valve)
		{
			valvesClosed++;
			it.Visual.Color = new Color(0.2f, 0.85f, 0.3f);
			ShowToast(valvesClosed >= ValveCount ? "All valves closed. The leak is stopped." : $"Valve closed ({valvesClosed}/{ValveCount})");
		}
		else if (it.Kind == InteractKind.Filter)
		{
			filtersUsed++;
			oxygen = Mathf.Min(OxygenMax, oxygen + FilterOxygenBonus);
			it.Visual.Color = new Color(0.25f, 0.27f, 0.3f);
			ShowToast($"Carbon filter replaced: +{FilterOxygenBonus:F0} oxygen");
		}

		hudPrompt.Text = "";
		UpdateObjective();
		CheckCompletion();
	}

	private void CheckCompletion()
	{
		if (state != PhaseState.Playing) return;
		if (valvesClosed >= ValveCount && terminalDone)
		{
			state = PhaseState.Completed;
			if (player != null) player.Locked = true;
			ShowSuccessScreen();
		}
	}

	// ------------------------------------------------------------------ RADIO

	private void UpdateRadio()
	{
		if (terminalDone) return;
		if (missionTime >= nextRadioAt)
		{
			nextRadioAt += RadioInterval;
			SendRadioMessage();
		}
	}

	private void SendRadioMessage()
	{
		float dread = GetDread();
		float garble = 0.12f + 0.45f * dread;
		string msg;
		if (rng.Randf() < 0.15f * dread)
			msg = "HOUSTON: ...kkzzt... [static] ...zzz...";
		else if (dread > 0.5f && rng.Randf() < 0.6f)
			msg = $"HOUSTON: reset code {Garble(garble)} ... {Garble(garble)} ...copy?";
		else
			msg = $"HOUSTON: ECLSS reset code {Garble(garble)}. Over.";

		radioLog.Add(msg);
		if (radioLog.Count > 4) radioLog.RemoveAt(0);
		ShowToast("Incoming transmission from Houston");
	}

	private string Garble(float p)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < resetCode.Length; i++)
		{
			sb.Append(rng.Randf() < p ? '#' : resetCode[i]);
			if (i < resetCode.Length - 1) sb.Append(' ');
		}
		return sb.ToString();
	}

	// ------------------------------------------------------------------ TERMINAL

	private void SetupTerminal()
	{
		terminalLayer = new CanvasLayer { Name = "Terminal", Layer = 60, Visible = false };
		AddChild(terminalLayer);

		var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f) };
		terminalLayer.AddChild(dim);
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var center = new CenterContainer();
		dim.AddChild(center);
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
		center.AddChild(box);

		box.AddChild(new Label { Text = "ECLSS SOFT-RESET (Upper Level Terminal)", HorizontalAlignment = HorizontalAlignment.Center });
		box.AddChild(new Label
		{
			Text = "Type the 4-digit reset code from Houston.\nEnter = confirm | Backspace = delete | Esc = close",
			HorizontalAlignment = HorizontalAlignment.Center
		});

		terminalCodeLabel = new Label { Text = "_ _ _ _", HorizontalAlignment = HorizontalAlignment.Center };
		terminalCodeLabel.AddThemeFontSizeOverride("font_size", 36);
		box.AddChild(terminalCodeLabel);

		terminalBar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0, CustomMinimumSize = new Vector2(0, 24), ShowPercentage = false };
		box.AddChild(terminalBar);

		terminalStatusLabel = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		box.AddChild(terminalStatusLabel);
	}

	private void OpenTerminal()
	{
		if (terminalDone) return;
		terminalOpen = true;
		terminalResetting = false;
		terminalResetProgress = 0f;
		terminalInput = "";
		terminalBar.Value = 0;
		terminalStatusLabel.Text = "Waiting for code...";
		RefreshTerminalCode();
		terminalLayer.Visible = true;
		if (player != null) player.Locked = true;
	}

	private void CloseTerminal()
	{
		terminalOpen = false;
		terminalResetting = false;
		terminalLayer.Visible = false;
		if (player != null && state == PhaseState.Playing) player.Locked = false;
	}

	private void RefreshTerminalCode()
	{
		var parts = new string[4];
		for (int i = 0; i < 4; i++)
			parts[i] = i < terminalInput.Length ? terminalInput[i].ToString() : "_";
		terminalCodeLabel.Text = string.Join(" ", parts);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!terminalOpen || state != PhaseState.Playing) return;
		if (!(@event is InputEventKey k) || !k.Pressed || k.Echo) return;

		if (k.Keycode == Key.Escape)
		{
			CloseTerminal();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (terminalResetting || terminalDone) return;

		if (k.Keycode >= Key.Key0 && k.Keycode <= Key.Key9)
		{
			TypeDigit((int)k.Keycode - (int)Key.Key0);
		}
		else if (k.Keycode >= Key.Kp0 && k.Keycode <= Key.Kp9)
		{
			TypeDigit((int)k.Keycode - (int)Key.Kp0);
		}
		else if (k.Keycode == Key.Backspace)
		{
			if (terminalInput.Length > 0)
				terminalInput = terminalInput.Substring(0, terminalInput.Length - 1);
			RefreshTerminalCode();
		}
		else if (k.Keycode == Key.Enter || k.Keycode == Key.KpEnter)
		{
			SubmitTerminalCode();
		}
		else
		{
			return;
		}
		GetViewport().SetInputAsHandled();
	}

	private void TypeDigit(int digit)
	{
		if (terminalInput.Length >= 4) return;
		terminalInput += digit.ToString();
		terminalStatusLabel.Text = "";
		RefreshTerminalCode();
	}

	private void SubmitTerminalCode()
	{
		if (terminalInput == resetCode)
		{
			terminalResetting = true;
			terminalResetProgress = 0f;
			terminalStatusLabel.Text = "Re-initializing life support...";
		}
		else
		{
			terminalStatusLabel.Text = "INVALID CODE";
			terminalInput = "";
			RefreshTerminalCode();
		}
	}

	private void UpdateTerminal(float dt)
	{
		if (!terminalOpen) return;

		if (terminalResetting)
		{
			terminalResetProgress += dt;
			terminalBar.Value = Mathf.Min(100f, terminalResetProgress / TerminalResetTime * 100f);
			if (terminalResetProgress >= TerminalResetTime)
			{
				terminalResetting = false;
				terminalDone = true;
				terminalCloseTimer = 1.2f;
				terminalStatusLabel.Text = "CODE ACCEPTED";
				if (terminalInteractable != null)
				{
					terminalInteractable.Done = true;
					terminalInteractable.Visual.Color = new Color(0.2f, 0.85f, 0.3f);
				}
				UpdateObjective();
			}
		}
		else if (terminalDone)
		{
			terminalCloseTimer -= dt;
			if (terminalCloseTimer <= 0f)
			{
				CloseTerminal();
				CheckCompletion();
			}
		}
	}

	// ------------------------------------------------------------------ END SCREENS

	private void SetupScreens()
	{
		var screenLayer = new CanvasLayer { Name = "Screens", Layer = 110 };
		AddChild(screenLayer);

		screenOverlay = new ColorRect { Color = new Color(0, 0, 0, 0.8f), Visible = false };
		screenLayer.AddChild(screenOverlay);
		screenOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var center = new CenterContainer();
		screenOverlay.AddChild(center);
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0), Alignment = BoxContainer.AlignmentMode.Center };
		center.AddChild(box);

		screenTitle = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		box.AddChild(screenTitle);
		screenReason = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		box.AddChild(screenReason);
		screenDetails = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(screenDetails);

		screenBtnRetry = new Button { Text = "Retry" };
		screenBtnRetry.Pressed += () => GetTree().ReloadCurrentScene();
		box.AddChild(screenBtnRetry);

		screenBtnMenu = new Button { Text = "Back to Menu" };
		screenBtnMenu.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
		box.AddChild(screenBtnMenu);
	}

	private void Fail(string reason)
	{
		if (state != PhaseState.Playing) return;
		state = PhaseState.Failed;
		if (terminalOpen) CloseTerminal();
		if (player != null) player.Locked = true;
		hudPrompt.Text = "";

		screenOverlay.Visible = true;
		screenTitle.Text = "MISSION FAILED";
		screenReason.Text = reason;
		screenDetails.Text = "The crew could not restore the air in time.";
		screenBtnRetry.Visible = true;
		UpdateObjective();
	}

	private void ShowSuccessScreen()
	{
		screenOverlay.Visible = true;
		screenTitle.Text = "PHASE 2 COMPLETE";
		screenReason.Text = "Leak sealed. Life support restored.";
		screenDetails.Text = $"Time: {missionTime:F1}s | Oxygen left: {oxygen:F0}% | Energy: 510 kWh | Comms: intermittent (7s latency) | Mass: ~178,000 kg | Cost: US$ 320M";
		screenBtnRetry.Visible = false;
		UpdateObjective();
	}
}
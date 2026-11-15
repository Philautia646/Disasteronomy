using Godot;
using System;

public partial class EvaPlayer : CharacterBody2D
{
	public enum PlayerState { Floating, Grabbed }

	[Export] public float MaxDriftSpeed = 80f;
	[Export] public float SuitAcceleration = 60f;
	[Export] public float ArmMaxLength = 160f;
	[Export] public float GrabPullSpeed = 240f;
	[Export] public float RailSlideSpeed = 90f;
	[Export] public int SaferChargesDefault = 3;
	[Export] public float SaferImpulse = 140f;
	[Export] public float SaferCooldown = 1.0f;
	[Export] public float TetherMaxLength = 300f;
	[Export] public float TetherSnapRatio = 1.35f;
	[Export] public float TetherSnapTime = 0.5f;
	[Export] public float TetherElasticStrength = 90f;
	[Export] public Rect2 LevelBounds = new Rect2(-600, -450, 2200, 1300);
	[Export] public float OutOfBoundsTimeLimit = 3f;
	[Export] public float GrabRadius = 30f;
	[Export] public bool DebugKeys = true; // F9 = teleport near the CPS

	private Line2D tetherLine;
	private Line2D armLine;
	private MultiplayerSynchronizer sync;
	private Label nicknameLabel;
	public Camera2D playerCamera;
	private Phase1Manager manager;

	public PlayerState state = PlayerState.Floating;
	private Handrail currentRail;
	private Vector2 armTip = Vector2.Zero;
	private Vector2 armDirection = Vector2.Down;

	// Grab / pull
	private bool pulling = false;
	private Vector2 pullTarget = Vector2.Zero;
	private Vector2 lastSlideVelocity = Vector2.Zero;

	// Tether: anchored at the airlock at first, then at the last handrail point the player held.
	private bool tetherInitialized = false;
	private Vector2 tetherAnchorPos = Vector2.Zero;
	private float tetherDistance = 0f;

	private int saferCharges;
	private float saferCooldownTimer = 0f;
	private float tetherStretchTimer = 0f;
	private float outOfBoundsTimer = 0f;

	private bool eWasDown = false;
	private bool f9WasDown = false;

	private Phase1Manager Manager
	{
		get
		{
			if (manager == null || !IsInstanceValid(manager))
			{
				Node n = GetParent();
				while (n != null)
				{
					if (n is Phase1Manager m) { manager = m; break; }
					n = n.GetParent();
				}
			}
			return manager;
		}
	}

	public override void _EnterTree()
	{
		if (int.TryParse(Name, out int peerId))
			SetMultiplayerAuthority(peerId, true);
	}

	public override void _Ready()
	{
		AddToGroup("network_players");

		foreach (Node node in GetTree().GetNodesInGroup("network_players"))
		{
			if (node == this || !(node is CharacterBody2D other)) continue;
			AddCollisionExceptionWith(other);
			other.AddCollisionExceptionWith(this);
		}

		var collision = new CollisionShape2D();
		collision.Shape = new CircleShape2D { Radius = 16f };
		AddChild(collision);

		tetherLine = new Line2D { Name = "TetherLine", Width = 2f };
		AddChild(tetherLine);

		armLine = new Line2D { Name = "ArmLine", Width = 4f, DefaultColor = Colors.Cyan, Visible = false };
		AddChild(armLine);

		nicknameLabel = new Label
		{
			Name = "NicknameLabel",
			Position = new Vector2(-50f, -48f),
			Size = new Vector2(100f, 20f),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		AddChild(nicknameLabel);

		playerCamera = new Camera2D { Name = "PlayerCamera" };
		playerCamera.PositionSmoothingEnabled = true;
		playerCamera.PositionSmoothingSpeed = 8f;
		AddChild(playerCamera);
		if (IsMultiplayerAuthority())
		{
			playerCamera.Enabled = true;
			playerCamera.MakeCurrent();
		}

		sync = new MultiplayerSynchronizer { Name = "Sync" };
		AddChild(sync);
		var replicationConfig = new SceneReplicationConfig();
		replicationConfig.AddProperty(new NodePath(".:position"));
		replicationConfig.PropertySetReplicationMode(new NodePath(".:position"), SceneReplicationConfig.ReplicationMode.Always);
		replicationConfig.AddProperty(new NodePath(".:velocity"));
		replicationConfig.PropertySetReplicationMode(new NodePath(".:velocity"), SceneReplicationConfig.ReplicationMode.Always);
		sync.ReplicationConfig = replicationConfig;

		try
		{
			var networkHandler = GetNode("/root/HighLevelNetworkHandler");
			long pid = 0;
			if (long.TryParse(Name, out pid))
			{
				string nick = networkHandler.Call("get_player_nickname", pid).AsString();
				nicknameLabel.Text = string.IsNullOrWhiteSpace(nick) ? $"Jogador {pid}" : nick;
			}
		}
		catch { nicknameLabel.Text = $"Jogador {Name}"; }

		saferCharges = SaferChargesDefault;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Multiplayer.HasMultiplayerPeer() && !IsMultiplayerAuthority()) return;

		float dt = (float)delta;
		var mgr = Manager;

		// Edge detection for E and F9 must run every frame, even while input is locked.
		bool eDown = Input.IsPhysicalKeyPressed(Key.E);
		bool ePressed = eDown && !eWasDown;
		eWasDown = eDown;

		bool f9Down = Input.IsPhysicalKeyPressed(Key.F9);
		bool f9Pressed = f9Down && !f9WasDown;
		f9WasDown = f9Down;

		saferCooldownTimer = Mathf.Max(0f, saferCooldownTimer - dt);

		bool locked = mgr != null && mgr.InputLocked;

		if (!locked)
		{
			if (DebugKeys && f9Pressed && mgr != null)
				DebugTeleportNearCps(mgr);

			if (state == PlayerState.Floating)
				UpdateFloating(dt);
			else
				UpdateGrabbed(dt);

			if (ePressed && mgr != null && mgr.IsPlayerNearCps(GlobalPosition))
				mgr.RequestMinigame();
		}
		else
		{
			armLine.Visible = false;
		}

		UpdateTether(dt);
		CheckBounds(dt);

		MoveAndSlide();
	}

	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, 14f, Colors.White);
		DrawRect(new Rect2(-10, 6, 20, 22), Colors.Gray);
		DrawLine(new Vector2(-10, 12), new Vector2(-20, 12), Colors.White, 2f);
		DrawLine(new Vector2(10, 12), new Vector2(20, 12), Colors.White, 2f);
		DrawLine(new Vector2(-8, 24), new Vector2(-8, 32), Colors.White, 2f);
		DrawLine(new Vector2(8, 24), new Vector2(8, 32), Colors.White, 2f);
	}

	// Reads WASD + arrow keys by PHYSICAL key, so it does not depend on the InputMap
	// (ui_left / ui_right had no A / D bound, which is why A and D did nothing).
	private static Vector2 ReadMoveInput()
	{
		float x = 0f;
		float y = 0f;
		if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) x -= 1f;
		if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) x += 1f;
		if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) y -= 1f;
		if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) y += 1f;
		Vector2 v = new Vector2(x, y);
		return v.LengthSquared() > 1f ? v.Normalized() : v;
	}

	private static bool GrabHeld() => Input.IsMouseButtonPressed(MouseButton.Left);

	private void UpdateFloating(float dt)
	{
		Vector2 inputDir = ReadMoveInput();

		if (inputDir != Vector2.Zero)
		{
			// Suit thrusters never push you ABOVE MaxDriftSpeed, but they can still brake
			// a faster speed (e.g. after a SAFER burst) without clamping it down instantly.
			float before = Velocity.Length();
			Velocity += inputDir * SuitAcceleration * dt;
			Velocity = Velocity.LimitLength(Mathf.Max(MaxDriftSpeed, before));
		}

		if (Input.IsActionJustPressed("pular") && saferCharges > 0 && saferCooldownTimer <= 0f)
		{
			Vector2 toCursor = GetGlobalMousePosition() - GlobalPosition;
			if (toCursor.Length() > 0.01f) toCursor = toCursor.Normalized();
			Velocity -= toCursor * SaferImpulse; // jet toward the cursor, push the opposite way
			saferCharges--;
			saferCooldownTimer = SaferCooldown;
		}

		if (GrabHeld())
		{
			UpdateArm();
			CheckGrab(null);
			armLine.Points = new Vector2[] { Vector2.Zero, armTip - GlobalPosition };
			armLine.Visible = state == PlayerState.Floating;
		}
		else
		{
			armTip = GlobalPosition;
			armLine.Visible = false;
		}
	}

	private void UpdateGrabbed(float dt)
	{
		if (currentRail == null || !IsInstanceValid(currentRail))
		{
			ReleaseRail();
			return;
		}

		if (!GrabHeld())
		{
			ReleaseRail();
			return;
		}

		Velocity = Vector2.Zero;

		if (pulling)
		{
			Vector2 to = pullTarget - GlobalPosition;
			float step = GrabPullSpeed * dt;
			if (to.Length() <= step)
			{
				GlobalPosition = pullTarget;
				pulling = false;
			}
			else
			{
				GlobalPosition += to.Normalized() * step;
			}
			armLine.Points = new Vector2[] { Vector2.Zero, pullTarget - GlobalPosition };
			armLine.Visible = true;
			lastSlideVelocity = Vector2.Zero;
			return;
		}

		armLine.Visible = false;

		string axis = currentRail.GetAxis();
		Vector2 inputDir = ReadMoveInput();
		Vector2 slideDir = axis == "horizontal"
			? new Vector2(inputDir.X, 0f)
			: new Vector2(0f, inputDir.Y);

		lastSlideVelocity = Vector2.Zero;
		if (slideDir != Vector2.Zero)
		{
			slideDir = slideDir.Normalized();
			lastSlideVelocity = slideDir * RailSlideSpeed;
			GlobalPosition += lastSlideVelocity * dt;
		}

		// Stay glued to the rail (also stops sliding past the ends).
		GlobalPosition = currentRail.GetClosestGrabPoint(GlobalPosition);

		// The tether clips to the rail you are holding.
		tetherAnchorPos = GlobalPosition;

		// While holding the button, you may reach for ANOTHER rail (e.g. at a corner).
		UpdateArm();
		CheckGrab(currentRail);
	}

	private void ReleaseRail()
	{
		if (state == PlayerState.Grabbed)
			Velocity = lastSlideVelocity; // keep a little inertia when letting go
		state = PlayerState.Floating;
		currentRail = null;
		pulling = false;
		armLine.Visible = false;
	}

	private void UpdateArm()
	{
		Vector2 cursorPos = GetGlobalMousePosition();
		Vector2 toCursor = cursorPos - GlobalPosition;
		float dist = toCursor.Length();

		if (dist > 0.01f)
			armDirection = toCursor / dist;

		float armLen = Mathf.Min(dist, ArmMaxLength);
		armTip = GlobalPosition + armDirection * armLen;
	}

	private void CheckGrab(Handrail ignore)
	{
		foreach (Node node in GetTree().GetNodesInGroup("handrail"))
		{
			if (!(node is Handrail rail) || rail == ignore) continue;
			Vector2 closest = rail.GetClosestGrabPoint(armTip);
			if (armTip.DistanceTo(closest) <= GrabRadius)
			{
				state = PlayerState.Grabbed;
				currentRail = rail;
				pullTarget = rail.GetClosestGrabPoint(GlobalPosition);
				pulling = true;
				Velocity = Vector2.Zero;
				tetherAnchorPos = pullTarget; // the cable clips to the rail the moment the arm touches it
				return;
			}
		}
	}

	private void UpdateTether(float dt)
	{
		if (!tetherInitialized)
		{
			var anchors = GetTree().GetNodesInGroup("tether_anchor");
			if (anchors.Count > 0 && anchors[0] is Node2D a)
			{
				tetherAnchorPos = a.GlobalPosition;
				tetherInitialized = true;
			}
			else
			{
				return;
			}
		}

		Vector2 toAnchor = tetherAnchorPos - GlobalPosition;
		float dist = toAnchor.Length();
		tetherDistance = dist;

		tetherLine.Points = new Vector2[] { Vector2.Zero, ToLocal(tetherAnchorPos) };

		float tension = dist / TetherMaxLength;
		tetherLine.DefaultColor = tension > 0.8f ? Colors.Red : (tension > 0.5f ? Colors.Yellow : new Color(0.6f, 0.6f, 0.6f));

		if (dist > TetherMaxLength)
		{
			Vector2 elasticForce = (toAnchor / dist) * TetherElasticStrength * (dist - TetherMaxLength) * dt;
			Velocity += elasticForce;

			if (dist > TetherMaxLength * TetherSnapRatio)
			{
				tetherStretchTimer += dt;
				if (tetherStretchTimer >= TetherSnapTime)
					Manager?.OnMissionFailed("Tether snapped!");
			}
			else
			{
				tetherStretchTimer = 0f;
			}
		}
		else
		{
			tetherStretchTimer = 0f;
		}
	}

	private void CheckBounds(float dt)
	{
		if (!LevelBounds.HasPoint(GlobalPosition))
		{
			outOfBoundsTimer += dt;
			if (outOfBoundsTimer >= OutOfBoundsTimeLimit)
				Manager?.OnMissionFailed("Out of bounds!");
		}
		else
		{
			outOfBoundsTimer = 0f;
		}
	}

	private void DebugTeleportNearCps(Phase1Manager mgr)
	{
		ReleaseRail();
		GlobalPosition = mgr.CpsPosition + new Vector2(-220f, 0f);
		Velocity = Vector2.Zero;
		tetherAnchorPos = GlobalPosition;
		tetherInitialized = true;
		GD.Print("[DEBUG] F9: teleported near the CPS");
	}

	public int GetSaferCharges() => saferCharges;

	public float GetTetherTension() => TetherMaxLength > 0f ? tetherDistance / TetherMaxLength : 0f;
}
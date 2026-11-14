using Godot;
using System;

public partial class EvaPlayer : CharacterBody2D
{
	public enum PlayerState { Floating, Grabbed }

	[Export] public float MaxDriftSpeed = 50f;
	[Export] public float SuitAcceleration = 12f;
	[Export] public float ArmMaxLength = 220f;
	[Export] public float GrabPullSpeed = 100f;
	[Export] public float RailSlideSpeed = 80f;
	[Export] public int SaferChargesDefault = 3;
	[Export] public float SaferImpulse = 380f;
	[Export] public float SaferCooldown = 1.5f;
	[Export] public float TetherMaxLength = 300f;
	[Export] public float TetherSnapRatio = 1.35f;
	[Export] public float TetherSnapTime = 0.5f;
	[Export] public float TetherElasticStrength = 90f;
	[Export] public Rect2 LevelBounds = new Rect2(-600, -450, 2200, 1300);
	[Export] public float OutOfBoundsTimeLimit = 3f;
	[Export] public float GrabRadius = 30f;

	private Node2D tetherAnchor;
	private Line2D tetherLine;
	private MultiplayerSynchronizer sync;
	private Label nicknameLabel;
	public Camera2D playerCamera;
	private Phase1Manager manager;

	public PlayerState state = PlayerState.Floating;
	private Handrail currentRail;
	private Vector2 armTip = Vector2.Zero;
	private Vector2 armDirection = Vector2.Down;

	private int saferCharges;
	private float saferCooldownTimer = 0f;
	private float tetherStretchTimer = 0f;
	private float outOfBoundsTimer = 0f;
	private bool atCps = false;

	public override void _Ready()
	{
		AddToGroup("network_players");

		foreach (Node node in GetTree().GetNodesInGroup("network_players"))
		{
			if (node == this || !(node is CharacterBody2D other)) continue;
			AddCollisionExceptionWith(other);
			other.AddCollisionExceptionWith(this);
		}

		var anchors = GetTree().GetNodesInGroup("tether_anchor");
		tetherAnchor = anchors.Count > 0 ? anchors[0] as Node2D : null;

		var collision = new CollisionShape2D();
		collision.Shape = new CircleShape2D { Radius = 16f };
		AddChild(collision);

		tetherLine = new Line2D { Name = "TetherLine", Width = 2f };
		AddChild(tetherLine);

		nicknameLabel = new Label
		{
			Name = "NicknameLabel",
			Position = new Vector2(0, -28f),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		AddChild(nicknameLabel);

		playerCamera = new Camera2D { Name = "PlayerCamera" };
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

		manager = GetNodeOrNull<Phase1Manager>("/root/Phase1Manager");
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!Multiplayer.HasMultiplayerPeer()) return;
		if (!IsMultiplayerAuthority()) return;

		saferCooldownTimer = Mathf.Max(0f, saferCooldownTimer - (float)delta);

		if (state == PlayerState.Floating)
			UpdateFloating(delta);
		else
			UpdateGrabbed(delta);

		UpdateTether(delta);
		CheckBounds(delta);
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

	private void UpdateFloating(double delta)
	{
		Vector2 inputDir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");

		if (inputDir != Vector2.Zero)
		{
			Velocity += inputDir * SuitAcceleration * (float)delta;
			Velocity = Velocity.LimitLength(MaxDriftSpeed);
		}

		if (Input.IsActionJustPressed("pular") && saferCharges > 0 && saferCooldownTimer <= 0f)
		{
			Vector2 toCursor = GetViewport().GetMousePosition() - GlobalPosition;
			if (toCursor.Length() > 0f) toCursor = toCursor.Normalized();
			Velocity -= toCursor * SaferImpulse;
			saferCharges--;
			saferCooldownTimer = SaferCooldown;
		}

		if (Input.IsActionPressed("grab"))
		{
			UpdateArm();
			CheckGrab();
		}
		else
		{
			armTip = GlobalPosition;
		}

		if (atCps && Input.IsActionJustPressed("inventario"))
			manager?.RequestMinigame();
	}

	private void UpdateGrabbed(double delta)
	{
		if (currentRail == null)
		{
			state = PlayerState.Floating;
			return;
		}

		string axis = currentRail.GetAxis();
		Vector2 inputDir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");

		Vector2 slideDir = axis == "horizontal"
			? new Vector2(inputDir.X, 0f)
			: new Vector2(0f, inputDir.Y);

		if (slideDir != Vector2.Zero)
		{
			slideDir = slideDir.Normalized();
			Position += slideDir * RailSlideSpeed * (float)delta;
		}

		if (!Input.IsActionPressed("grab"))
		{
			state = PlayerState.Floating;
			currentRail = null;
		}
	}

	private void UpdateArm()
	{
		Vector2 cursorPos = GetViewport().GetMousePosition();
		Vector2 toCursor = cursorPos - GlobalPosition;
		float dist = toCursor.Length();

		if (dist > 0f)
			armDirection = toCursor / dist;

		float armLen = Mathf.Min(dist, ArmMaxLength);
		armTip = GlobalPosition + armDirection * armLen;
	}

	private void CheckGrab()
	{
		if (currentRail != null) return;

		foreach (Node node in GetTree().GetNodesInGroup("handrail"))
		{
			if (!(node is Handrail rail)) continue;
			Vector2 closest = rail.GetClosestGrabPoint(armTip);
			if (armTip.DistanceTo(closest) <= GrabRadius)
			{
				state = PlayerState.Grabbed;
				currentRail = rail;
				Position = closest;
				Velocity = Vector2.Zero;
				return;
			}
		}
	}

	private void UpdateTether(double delta)
	{
		if (tetherAnchor == null) return;

		Vector2 anchorPos = tetherAnchor.GlobalPosition;
		Vector2 toAnchor = anchorPos - GlobalPosition;
		float dist = toAnchor.Length();

		tetherLine.Points = new Vector2[] { GlobalPosition, anchorPos };

		float tension = dist / TetherMaxLength;
		tetherLine.DefaultColor = tension > 0.8f ? Colors.Red : (tension > 0.5f ? Colors.Yellow : new Color(0.6f, 0.6f, 0.6f));

		if (dist > TetherMaxLength)
		{
			if (dist > 0f)
			{
				Vector2 elasticForce = (toAnchor / dist) * TetherElasticStrength * (dist - TetherMaxLength) * (float)delta;
				Velocity += elasticForce;
			}

			if (dist > TetherMaxLength * TetherSnapRatio)
			{
				tetherStretchTimer += (float)delta;
				if (tetherStretchTimer >= TetherSnapTime)
					manager?.OnMissionFailed("Tether snapped!");
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

	private void CheckBounds(double delta)
	{
		if (!LevelBounds.HasPoint(GlobalPosition))
		{
			outOfBoundsTimer += (float)delta;
			if (outOfBoundsTimer >= OutOfBoundsTimeLimit)
				manager?.OnMissionFailed("Out of bounds!");
		}
		else
		{
			outOfBoundsTimer = 0f;
		}
	}

	public void SetAtCps(bool value) => atCps = value;

	public int GetSaferCharges() => saferCharges;

	public override void _EnterTree()
	{
		if (int.TryParse(Name, out int peerId))
			SetMultiplayerAuthority(peerId, true);
	}
}

using Godot;
using System;

/// <summary>
/// Side-view character for Phase 2 (ISS Lab / ECLSS): walks, jumps, climbs racks (ladders).
/// Everything is built in code; the visual is a 32x32-ish placeholder drawn in _Draw
/// (replace with an AnimatedSprite2D when the sprites are ready).
/// </summary>
public partial class Phase2Player : CharacterBody2D
{
	[Export] public float MoveSpeed = 170f;
	[Export] public float GroundAccel = 1400f;
	[Export] public float GroundFriction = 1600f;
	[Export] public float WetAccel = 350f;
	[Export] public float WetFriction = 160f;
	[Export] public float AirAccel = 700f;
	[Export] public float AirFriction = 300f;
	[Export] public float Gravity = 1300f;
	[Export] public float MaxFallSpeed = 900f;
	[Export] public float JumpVelocity = -470f;
	[Export] public float ClimbSpeed = 110f;
	[Export] public float CoyoteTime = 0.1f;
	[Export] public float CameraZoom = 1.4f;
	/// <summary>If this SpriteFrames resource exists it is used instead of the placeholder drawing. Animations: idle, walk, jump, climb (sprites face RIGHT).</summary>
	[Export] public string SpriteFramesPath = "res://Sprites/player_phase2.tres";

	/// <summary>Set by the manager (terminal open, phase over): no input, but physics still runs.</summary>
	public bool Locked = false;
	public Camera2D playerCamera;

	private Phase2Manager manager;
	private bool climbing = false;
	private bool facingRight = true;
	private float coyote = 0f;
	private bool jumpWasDown = false;
	private AnimatedSprite2D sprite;

	public bool IsClimbing => climbing;

	private Phase2Manager Manager
	{
		get
		{
			if (manager == null || !IsInstanceValid(manager))
			{
				Node n = GetParent();
				while (n != null)
				{
					if (n is Phase2Manager m) { manager = m; break; }
					n = n.GetParent();
				}
			}
			return manager;
		}
	}

	public override void _Ready()
	{
		var shape = new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(18f, 30f) } };
		AddChild(shape);
		SetupSprite();

		// Layer 4 = player. Mask 3 = solid (1) + one-way platforms (2).
		CollisionLayer = 4;
		CollisionMask = 3;

		playerCamera = new Camera2D { Name = "PlayerCamera", Zoom = new Vector2(CameraZoom, CameraZoom) };
		playerCamera.PositionSmoothingEnabled = true;
		playerCamera.PositionSmoothingSpeed = 8f;
		var mgr = Manager;
		if (mgr != null)
		{
			playerCamera.LimitLeft = (int)mgr.LevelBounds.Position.X;
			playerCamera.LimitTop = (int)mgr.LevelBounds.Position.Y;
			playerCamera.LimitRight = (int)mgr.LevelBounds.End.X;
			playerCamera.LimitBottom = (int)mgr.LevelBounds.End.Y;
		}
		AddChild(playerCamera);
		playerCamera.Enabled = true;
		playerCamera.MakeCurrent();
	}

	private void SetupSprite()
	{
		if (string.IsNullOrEmpty(SpriteFramesPath) || !ResourceLoader.Exists(SpriteFramesPath)) return;
		var frames = GD.Load<SpriteFrames>(SpriteFramesPath);
		if (frames == null) return;
		sprite = new AnimatedSprite2D { Name = "Sprite", SpriteFrames = frames };
		AddChild(sprite);
		if (frames.HasAnimation("idle")) sprite.Play("idle");
	}

	private void UpdateAnimation(bool onFloor)
	{
		if (sprite == null) return;
		sprite.FlipH = !facingRight;
		string anim = climbing ? "climb" : (!onFloor ? "jump" : (Mathf.Abs(Velocity.X) > 10f ? "walk" : "idle"));
		if (!sprite.SpriteFrames.HasAnimation(anim)) anim = "idle";
		if (sprite.SpriteFrames.HasAnimation(anim) && (string)sprite.Animation != anim)
			sprite.Play(anim);
		sprite.SpeedScale = (climbing && Mathf.Abs(Velocity.Y) < 1f) ? 0f : 1f;
	}

	public override void _Draw()
	{
		if (sprite != null) return; // a real sprite is in use
		float dir = facingRight ? 1f : -1f;
		// SCBA tank on the back
		DrawRect(new Rect2(facingRight ? -13f : 7f, -4f, 6f, 14f), new Color(0.6f, 0.6f, 0.65f));
		// Legs
		DrawRect(new Rect2(-7f, 9f, 5f, 6f), new Color(0.25f, 0.3f, 0.2f));
		DrawRect(new Rect2(2f, 9f, 5f, 6f), new Color(0.25f, 0.3f, 0.2f));
		// Nomex suit body
		DrawRect(new Rect2(-8f, -3f, 16f, 13f), new Color(0.35f, 0.42f, 0.28f));
		// Head + positive-pressure mask
		DrawCircle(new Vector2(0f, -9f), 7f, new Color(0.85f, 0.85f, 0.88f));
		DrawRect(new Rect2(dir > 0 ? 0f : -7f, -11f, 7f, 6f), new Color(0.1f, 0.1f, 0.12f));
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		var mgr = Manager;

		float inputX = 0f;
		float inputY = 0f;
		bool jumpDown = false;
		if (!Locked)
		{
			if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) inputX -= 1f;
			if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) inputX += 1f;
			if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) inputY -= 1f;
			if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) inputY += 1f;
			jumpDown = Input.IsPhysicalKeyPressed(Key.Space);
		}
		bool jumpPressed = jumpDown && !jumpWasDown;
		jumpWasDown = jumpDown;

		float ladderX = GlobalPosition.X;
		bool onLadder = mgr != null && mgr.IsOnLadder(GlobalPosition, out ladderX);

		if (!climbing && onLadder && inputY != 0f)
			climbing = true;

		if (climbing && (!onLadder || jumpPressed))
		{
			bool jumpOff = jumpPressed;
			climbing = false;
			if (jumpOff)
				Velocity = new Vector2(inputX * MoveSpeed * 0.6f, JumpVelocity * 0.7f);
		}

		// While climbing, ignore one-way platforms so you can go up AND down through them.
		CollisionMask = climbing ? 1u : 3u;

		if (climbing)
		{
			Velocity = new Vector2(0f, inputY * ClimbSpeed);
			GlobalPosition = new Vector2(Mathf.MoveToward(GlobalPosition.X, ladderX, 200f * dt), GlobalPosition.Y);
			MoveAndSlide();
			if (inputY > 0f && IsOnFloor())
				climbing = false;
			UpdateAnimation(false);
			return;
		}

		bool onFloor = IsOnFloor();
		if (onFloor) coyote = CoyoteTime;
		else coyote -= dt;

		bool wet = onFloor && mgr != null && mgr.IsWet(GlobalPosition + new Vector2(0f, 16f));
		float accel = onFloor ? (wet ? WetAccel : GroundAccel) : AirAccel;
		float friction = onFloor ? (wet ? WetFriction : GroundFriction) : AirFriction;

		Vector2 v = Velocity;
		if (inputX != 0f)
		{
			v.X = Mathf.MoveToward(v.X, inputX * MoveSpeed, accel * dt);
			bool newFacing = inputX > 0f;
			if (newFacing != facingRight)
			{
				facingRight = newFacing;
				QueueRedraw();
			}
		}
		else
		{
			v.X = Mathf.MoveToward(v.X, 0f, friction * dt);
		}

		v.Y = Mathf.Min(v.Y + Gravity * dt, MaxFallSpeed);

		if (jumpPressed && coyote > 0f)
		{
			v.Y = JumpVelocity;
			coyote = 0f;
			Sfx.Play(this, "jump");
		}

		Velocity = v;
		MoveAndSlide();
		UpdateAnimation(IsOnFloor());
	}
}

using Godot;

public partial class Player : CharacterBody2D
{
	private const float MoveSpeed = 260f;
	private const float GroundAcceleration = 1800f;
	private const float AirAcceleration = 1000f;
	private const float GroundFriction = 2200f;
	private const float Gravity = 980f;
	private const float JumpVelocity = -600f;
	private const float CoyoteTime = 0.12f;
	private const float JumpBufferTime = 0.12f;
	private const int SpriteFrameWidth = 48;
	private const int SpriteFrameHeight = 64;

	private AnimatedSprite2D? sprite;
	private Label nicknameLabel;
	private Node networkHandler;
	private Callable nicknameChangedCallable;
	private long playerId;
	private string facing = "right";
	private float coyoteTimeRemaining;
	private float jumpBufferTimeRemaining;

	public override void _Ready()
	{
		AddToGroup("network_players");
		foreach (Node node in GetTree().GetNodesInGroup("network_players"))
		{
			if (node == this || node is not CharacterBody2D otherPlayer)
				continue;

			AddCollisionExceptionWith(otherPlayer);
			otherPlayer.AddCollisionExceptionWith(this);
		}

		nicknameLabel = GetNode<Label>("NicknameLabel");
		networkHandler = GetNode("/root/HighLevelNetworkHandler");
		nicknameChangedCallable = Callable.From<long, string>(OnPlayerNicknameChanged);
		networkHandler.Connect("player_nickname_changed", nicknameChangedCallable);
		if (long.TryParse(Name.ToString(), out playerId))
			UpdateNickname(networkHandler.Call("get_player_nickname", playerId).AsString());

		sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		AddJumpAnimation(
			"jump_left",
			"res://Sprites/The Female Adventurer - Free/The Female Adventurer - Free/Jump - NEW/Normal/Jump_Left_Down.png"
		);
		AddJumpAnimation(
			"jump_right",
			"res://Sprites/The Female Adventurer - Free/The Female Adventurer - Free/Jump - NEW/Normal/Jump_Right_Down.png"
		);

		var camera = GetNodeOrNull<Camera2D>("Camera2D");
		if (camera != null)
			camera.Enabled = IsMultiplayerAuthority();

		if (IsMultiplayerAuthority())
			GD.Print("Controlando meu personagem: " + Name);

		UpdateAnimation("idle_right");
	}

	public override void _ExitTree()
	{
		if (networkHandler != null &&
			networkHandler.IsConnected("player_nickname_changed", nicknameChangedCallable))
		{
			networkHandler.Disconnect("player_nickname_changed", nicknameChangedCallable);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Verificar se o multiplayer está inicializado
		if (Multiplayer.MultiplayerPeer == null)
			return;
		if (!IsMultiplayerAuthority())
			return;

		float horizontal = Input.GetAxis("ui_left", "ui_right");
		if (Mathf.Abs(horizontal) < 0.01f)
			horizontal = Input.GetAxis("mover_esquerda", "mover_direita");

		if (Mathf.Abs(horizontal) > 0.01f)
			facing = horizontal < 0 ? "left" : "right";

		float frameDelta = (float)delta;
		bool onFloor = IsOnFloor();
		coyoteTimeRemaining = onFloor
			? CoyoteTime
			: Mathf.Max(0, coyoteTimeRemaining - frameDelta);

		if (Input.IsActionJustPressed("pular"))
			jumpBufferTimeRemaining = JumpBufferTime;
		else
			jumpBufferTimeRemaining = Mathf.Max(0, jumpBufferTimeRemaining - frameDelta);

		float targetSpeed = horizontal * MoveSpeed;
		float acceleration = onFloor ? GroundAcceleration : AirAcceleration;
		float horizontalChange = Mathf.Abs(horizontal) < 0.01f && onFloor
			? GroundFriction
			: acceleration;
		float velocityY = Velocity.Y;

		if (onFloor && velocityY > 0)
			velocityY = 0;
		else if (!onFloor)
			velocityY += Gravity * frameDelta;

		if (jumpBufferTimeRemaining > 0 && coyoteTimeRemaining > 0)
		{
			velocityY = JumpVelocity;
			jumpBufferTimeRemaining = 0;
			coyoteTimeRemaining = 0;
		}

		Velocity = new Vector2(
			Mathf.MoveToward(Velocity.X, targetSpeed, horizontalChange * frameDelta),
			velocityY
		);
		MoveAndSlide();

		if (!IsOnFloor())
			UpdateAnimation("jump_" + facing);
		else if (Mathf.Abs(Velocity.X) > 1f)
			UpdateAnimation("walk_" + facing);
		else
			UpdateAnimation("idle_" + facing);
	}

	private void AddJumpAnimation(string animationName, string texturePath)
	{
		if (sprite == null)
			return;

		SpriteFrames frames = sprite.SpriteFrames;
		if (frames.HasAnimation(animationName))
			return;

		Texture2D texture = GD.Load<Texture2D>(texturePath);
		if (texture == null || texture.GetWidth() % SpriteFrameWidth != 0 ||
			texture.GetHeight() < SpriteFrameHeight)
		{
			GD.PushError($"A textura da animação {animationName} está ausente ou tem dimensões inválidas: {texturePath}");
			return;
		}

		frames.AddAnimation(animationName);
		frames.SetAnimationSpeed(animationName, 8f);
		frames.SetAnimationLoopMode(animationName, SpriteFrames.LoopMode.Linear);
		int frameCount = texture.GetWidth() / SpriteFrameWidth;
		for (int index = 0; index < frameCount; index++)
		{
			var atlas = new AtlasTexture
			{
				Atlas = texture,
				Region = new Rect2(
					index * SpriteFrameWidth,
					0,
					SpriteFrameWidth,
					SpriteFrameHeight
				)
			};
			frames.AddFrame(animationName, atlas);
		}
	}

	private void UpdateAnimation(string animationName)
	{
		if (sprite == null)
			return;

		if (sprite.Animation != animationName)
			sprite.Play(animationName);
	}

	private void OnPlayerNicknameChanged(long changedPlayerId, string nickname)
	{
		if (changedPlayerId == playerId)
			UpdateNickname(nickname);
	}

	private void UpdateNickname(string nickname)
	{
		nicknameLabel.Text = string.IsNullOrWhiteSpace(nickname)
			? $"Jogador {playerId}"
			: nickname;
	}

	public override void _EnterTree()
	{
		if (int.TryParse(Name.ToString(), out int peerId))
			SetMultiplayerAuthority(peerId, true);
	}
}

# SCOPO.md — Progresso do Projeto Disasteronomy

> Documento de acompanhamento do desenvolvimento. Atualizado conforme avanços nas sprints.

---

## 1. Visão Geral

| Item | Detalhe |
|------|---------|
| **Nome do Projeto** | Disasteronomy |
| **Engine** | Godot 4.7 |
| **Linguagem Principal** | C# (.NET) + GDScript (scripts auxiliares de rede/UI) |
| **Plataforma Alvo** | Desktop (Windows) |
| **Renderização** | Forward Plus, HDR 2D, Pixel Art (filtro nearest, sem mipmaps) |
| **Branche Atual** | `main` |
| **Último Commit** | `6170ccb882cf038b08f386b0c7a5c21df76c08a7` |

---

## 2. Arquitetura e Configuração

### project.godot (configurado)
- `config/name = "disasteronomy"`
- `run/main_scene = "res://Scenes/MainMenu.tscn"`
- `config/features = ["4.7", "C#", "Forward Plus"]`
- `window/stretch/mode = "canvas_items"`, `shrink = 1`
- `viewport/hdr_2d = true`
- Texturas: filtro nearest (0), sem repeat, sem anisotrópico, sem mipmaps

### Autoloads (singletons)
| Autoload | Caminho | Linguagem | Responsabilidade |
|----------|---------|-----------|------------------|
| `HighLevelNetworkHandler` | `res://Scripts/high_level_network_handler.gd` | GDScript | Rede multiplayer (ENet), descoberta LAN, códigos de sala, nicknames |
| `SceneTransitions` | `res://Scripts/Core/SceneTransitions.cs` | C# | Troca de cenas (`ChangeSceneToFile`) |
| `Global` | `res://Scripts/Core/Global.cs` | C# | Sistema de save/load (arquivo `user://disasteronomy_save.dat`) |

### Input Maps configurados
`ui_left`, `ui_right`, `ui_up`, `ui_down`, `mover_esquerda` (A), `mover_direita` (D), `pular` (Espaço), `correr` (Shift), `dash` (Mouse direito), `inventario` (E), `voice_chat` (V), `grab` (Mouse esquerdo — registrado em runtime via `InputMap.AddAction`)

## 3. Scripts Implementados

### C# (`.cs`)

#### `Scripts/Core/Global.cs`
- ✅ `ExisteSave()` — verifica se arquivo de save existe
- ✅ `Salvar()` — grava timestamp + versão do save
- ✅ `Carregar()` — lê save existente (timestamp + versão)

#### `Scripts/Core/SceneTransitions.cs`
- ✅ `ChangeScene(path)` — delega para `GetTree().ChangeSceneToFile(path)`

#### `Scripts/Player.cs`
- ✅ `CharacterBody2D` com movimento WASD/setas (velocidade 200)
- ✅ `AnimatedSprite2D` com animações `idle_*` e `walk_*` por direção (up/down/left/right)
- ✅ Nickname label sincronizado via `HighLevelNetworkHandler`
- ✅ Câmera ativada apenas para quem tem multiplayer authority
- ✅ Collision exceptions entre jogadores de rede (evita colisão entre peers)
- ✅ `_EnterTree` define multiplayer authority pelo ID do peer

#### `Scripts/UI/MainMenu.cs`
- ⚠️ Referencia `res://Scenes/Game.tscn` (cena **não existe** no repositório)
- ✅ `_OnStartButtonPressed()` — troca para cena do jogo
- ✅ `_OnQuitButtonPressed()` — encerra aplicação

#### `Scripts/VoiceChat.cs`
- ✅ Captura de microfone via `AudioEffectCapture` no barramento "Microphone"
- ✅ Push-to-talk (tecla V)
- ✅ Encode/Decode de áudio PCM 16-bit mono
- ✅ Broadcast de voz via RPC (canal não confiável, unreliable)
- ✅ Reprodução remota via `AudioStreamGeneratorPlayback` por peer
- ✅ Indicador visual de microfone ligado/desligado

### Phase 1 — `Scripts/Phase1/` (C#)

#### `Scripts/Phase1/Handrail.cs`
- ✅ `Area2D` que desenha uma barra amarela com `_Draw`
- ✅ Adiciona-se ao grupo `"handrail"`
- ✅ `GetClosestGrabPoint(Vector2)` — projeta um ponto no segmento da barra
- ✅ `GetAxis()` — retorna `"horizontal"` ou `"vertical"`

#### `Scripts/Phase1/EvaPlayer.cs`
- ✅ `CharacterBody2D` com estados `Floating` e `Grabbed`
- ✅ Floating: inércia pura, WASD adiciona ajuste fraco limitado por `MaxDriftSpeed`
- ✅ Segurar `"grab"` estende o braço em direção ao cursor até `ArmMaxLength`
- ✅ Se a ponta do braço tocar um Handrail, muda para `Grabbed` e puxa o jogador para o ponto
- ✅ Grabbed: desliza ao longo do trilho com A/D (W/S para verticais) a velocidade constante, sem inércia
- ✅ Soltar `"grab"` retorna a `Floating` mantendo a velocidade atual (lançamento entre trilhos)
- ✅ SAFER no Space: impulso oposto à direção do cursor, custa 1 carga (padrão 3), cooldown curto
- ✅ Tether: `Line2D` do jogador ao `TetherAnchor`; força elástica além de `TetherMaxLength`; rompe se esticar além de `TetherSnapRatio` (1.35) por mais de 0.5s
- ✅ Falha após 3s fora dos limites do nível (`LevelBounds`)
- ✅ Nickname label, câmera apenas para authority, authority por peer ID, exceções de colisão entre peers
- ✅ Netcode: apenas a authority simula; `MultiplayerSynchronizer` sincroniza posição e velocidade

#### `Scripts/Phase1/CpsMinigame.cs`
- ✅ `CanvasLayer` UI — abre quando jogador está dentro do trigger CPS e pressiona E
- ✅ Calcula ângulo do cursor relativo ao centro do painel a cada frame
- ✅ Velocidade angular com média móvel de ~0.15s, ignora saltos grandes de ângulo
- ✅ Zones: congelar abaixo de 90°/s, ideal 180-360°/s, superaquecer acima de 540°/s
- ✅ Barra de progresso (0-100) e barra de temperatura (azul/verde/vermelho)
- ✅ Zona ideal dá progresso rápido e temperatura estável
- ✅ Congelado ou superaquecido por mais de 2s custa 30% de progresso
- ✅ 3 pinos em sequência (\"Pin 1/3\" etc.); emite `MinigameCompleted` ao terminar
- ✅ Ponteiro rotativo no painel

#### `Scripts/Phase1/Phase1Manager.cs`
- ✅ Máquina de estados: `Airlock` → `Transit` → `CpsArrival` → `Minigame` → `Completed` / `Failed`
- ✅ HUD: cargas SAFER, tensão do tether, objetivo atual, timer da missão
- ✅ Tela de falha: \"MISSION FAILED\" com motivo (tether rompido / fora dos limites), botões Retry e Back to Menu
- ✅ Tela de sucesso: \"PHASE 1 COMPLETE\" com tempo real, energia 160 kWh, comms estável com latência 1.3-2s, massa ~115.000 kg
- ✅ Multiplayer: minigame local por jogador; fase completa quando todos os jogadores conectados (máx 2) terminam
- ✅ Sincronização de conclusão via RPC confiável
- ✅ Construção de nível em código no `_Ready` (espaço, estrelas, airlock, CPS, trilhos, âncora do tether, trigger CPS)

### GDScript (`.gd`)

#### `Scripts/high_level_network_handler.gd`
- ✅ Porta servidor: 7777 (ENet), porta descoberta: 7778 (UDP broadcast)
- ✅ `start_server(room_code)` — cria servidor ENet + descoberta LAN, gera código de 6 dígitos se vazio
- ✅ `start_client(ip)` — conecta como cliente ENet
- ✅ `start_client_by_room_code(code)` — busca sala na LAN e conecta
- ✅ `discover_rooms()` — varredura LAN com timeout de 3s
- ✅ Sinais: `server_started`, `peer_activity`, `room_created`, `room_found`, `room_search_completed`, `player_nickname_changed`
- ✅ Gerenciamento de nicknames (máximo 16 caracteres, salvo em `user://player_profile.cfg`)
- ✅ Sincronização de nicknames entre peers via RPC

#### `Scripts/high_level_ui.gd`
- ✅ Interface de lobby multiplayer: input de nickname, código de sala, lista de salas descobertas
- ✅ Botões: Iniciar Servidor, Conectar como Cliente, Buscar Salas
- ✅ Log de atividade (entrada/saída de peers)
- ✅ Seleção de sala da lista préenche o campo de código

#### `Scripts/high_level_multiplayer-spawner.gd`
- ✅ Estende `MultiplayerSpawner`
- ✅ Spawna `res://Scenes/Player.tscn` ao conectar peer
- ✅ Spawna jogador do servidor ao iniciar (`spawn_server_player`)
- ✅ Remove jogador ao desconectar (`remove_player`)
- ⚠️ `spawn_path` não definido — usa nó pai como fallback

#### `Scripts/MainMenu.gd`
- ✅ Menu principal com botões: Iniciar Jogo, Configurações, Créditos, Sair
- ✅ Modal de save: Novo Jogo, Continuar Save, Cancelar
- ✅ Botão "Continuar" desabilitado se não houver save (via `Global.ExisteSave()`)
- ✅ Transições de cena via `SceneTransitions`

#### `Scripts/BackToMainMenu.gd`
- ✅ Botão de voltar para `res://Scenes/MainMenu.tscn`

## 4. Cenas (`.tscn`)

| Cena | Caminho | Status | Observações |
|------|---------|--------|-------------|
| Main Menu | `res://Scenes/MainMenu.tscn` | ✅ Completo | Botões estilizados, modal de save |
| Multiplayer Menu | `res://cenas/MultiplayerMenu.tscn` | ⚠️ Parcial | Referenciada, mas não inspecionada |
| Settings Menu | `res://cenas/SettingsMenu.tscn` | ⚠️ Parcial | Referenciada, mas não inspecionada |
| Credits Menu | `res://cenas/CreditsMenu.tscn` | ⚠️ Parcial | Referenciada, mas não inspecionada |
| Multiplayer Game | `res://Scenes/multiplayer.tscn` | ⚠️ Parcial | Cena de jogo multiplayer, não inspecionada |
| Player | `res://Scenes/Player.tscn` | ⚠️ Parcial | Usada pelo spawner, não inspecionada |
| Phase 1 | `res://Scenes/Phase1/Phase1.tscn` | ✅ Completo | Fase 1 \"Vacuum Calibration\" implementada |
| Game (referenciado antigo) | `res://Scenes/Game.tscn` | ❌ Removido | Substituído por Phase1.tscn; MainMenu.cs corrigido |
| Teste | `res://teste.tscn` | ⚠️ Parcial | Cena de teste, não inspecionada |

## 5. Assets

| Tipo | Caminho | Status |
|------|---------|--------|
| Música (loops OGG/WAV) | `Music/Loops - Lonely Nightmare/` | ✅ Presente |
| Sprites UI / MainMenu | `Sprites/UI/MainMenu/` | ✅ Presente (botões, fundo, etc.) |
| Sprites Personagem | `Sprites/The Female Adventurer - Free/` | ✅ Presente (animação de walk) |
| Áudio bus layout | `default_bus_layout.tres` | ✅ Configurado (barramento Microphone) |

## 6. Funcionalidades por Status

### ✅ Implementadas e funcionais
1. Menu principal com navegação e modal de save/load
2. Sistema de save/load básico (timestamp + versão)
3. Rede multiplayer ENet (servidor/cliente)
4. Descoberta de salas na LAN via UDP broadcast
5. Códigos de sala de 6 dígitos
6. Spawn/despawn de jogadores em rede
7. Movimento do jogador com animações direcionais
8. Nickname sincronizado entre peers
9. Sistema de voz (push-to-talk) com transmissão RPC
10. Input mapeado (movimento, dash, inventário, voz)
11. Transições de cena
12. **Phase 1: Gravidade zero com inércia pura**
13. **Phase 1: Braço de alcance com captura de trilhos (handrails)**
14. **Phase 1: Tether de segurança com ruptura elástica**
15. **Phase 1: SAFER jetpack com cargas limitadas (3)**
16. **Phase 1: Minigame CPS de 3 pinos (rotação circular)**
17. **Phase 1: Telas de sucesso e falha com HUD**
18. **Phase 1: Multiplayer para até 2 jogadores**

### ⚠️ Parcialmente implementadas / referenciadas mas incompletas
1. Cenas de menu secundário (Settings, Credits, Multiplayer) — existem como arquivos `.tscn` mas não foram inspecionadas detalhadamente
2. `spawn_path` no MultiplayerSpawner para Phase1 — definido em código por Phase1Manager
3. Save armazena apenas timestamp/versão — sem dados reais de progresso do jogador

### ❌ Não implementadas / pendentes
1. Mundo / níveis / cenários além da Phase 1
2. Lógica de desastres (tema central do jogo)
3. Sistema de inventário (input mapeado mas sem lógica)
4. Menu de configurações funcional
5. Tela de créditos funcional
6. Build/export automatizado para Windows
7. Testes unitários ou de integração

## 7. Próximos Passos Sugeridos

1. **Implementar mundo/jogo** — cenários, colisões, interações além da Phase 1
2. **Implementar sistema de desastres** — mecânica central do jogo
3. **Completar save/load** — armazenar progresso real do jogador
4. **Revisar cenas secundárias** — Settings, Credits, Multiplayer Menu
5. **Configurar exportação Windows** — verificar `export_presets.cfg`
6. **Adicionar .csproj / .sln** — mencionado no README, não encontrado no repositório
7. **Documentar API de rede** — sinais e RPCs do `HighLevelNetworkHandler`

## 8. Notas de Desenvolvimento

- Mistura de C# (lógica de jogo, jogador, voz, save) e GDScript (rede, UI de lobby)
- O projeto foi configurado no Godot Editor; editar `project.godot` manualmente não é recomendado
- Voz usa `AudioEffectCapture` — requer barramento "Microphone" configurado no `default_bus_layout.tres`
- Rede usa ENet (UDP) — portas 7777 (jogo) e 7778 (descoberta) devem estar liberadas no firewall
- Caminhos de cena usam `res://` (caminhos absolutos do projeto Godot)

### Phase 1 Notes (decisões de implementação)
- Input action `"grab"` (botão esquerdo do mouse) registrado em runtime via `InputMap.AddAction`, não editando `project.godot`
- Todos os visuais da Phase 1 desenhados com primitivas (ColorRect, Line2D, _Draw) para substituição futura por sprites
- Conteúdo da cena construído em código no `_Ready` da Phase1Manager para evitar arquivos .tscn quebrados
- EvaPlayer.tscn cena mínima criada apenas para satisfazer o MultiplayerSpawner; colisão e nós adicionais criados em código
- MainMenu.cs corrigido para apontar para `res://Scenes/Phase1/Phase1.tscn` via SceneTransitions
- Netcode: apenas a authority de cada EvaPlayer simula; MultiplayerSynchronizer replica posição e velocidade
- Minigame CPS é local por jogador; conclusão sincronizada via RPC; fase completa quando todos os peers terminam
- Em solo (sem MultiplayerPeer), apenas um jogador é spawnado

### Known Limitations
- Sem .csproj/.sln no repositório; build via `dotnet build` requer configuração do SDK Godot.NET (dotnet não instalado neste ambiente)
- Teste headless do Godot não foi executado neste ambiente
- Handrails exigem posicionamento preciso; gaps entre trilhos podem exigir uso do SAFER
- Câmera segue apenas o jogador com authority local (cada cliente vê seu próprio jogador)

### Godot 4 C# API Fixes (applied)
- `GetViewport().Size` → `GetViewport().GetVisibleRect().Size` (CpsMinigame.cs, Phase1Manager.cs)
- `GetGlobalMousePosition()` → `GetViewport().GetMousePosition()` (CpsMinigame.cs, EvaPlayer.cs)
- `Mathf.Rad2Deg(value)` → `Mathf.RadToDeg(value)` (CpsMinigame.cs)
- `Camera2D.Current` → `camera.Enabled = true; camera.MakeCurrent()` (EvaPlayer.cs)
- `ReplicationConfig`/`SyncMode` → `SceneReplicationConfig` with `AddProperty`/`PropertySetReplicationMode` (EvaPlayer.cs)
- `Vector2.Clamped()` → `Velocity.LimitLength()` (EvaPlayer.cs)
- `Multiplayer.MultiplayerPeer` → `Multiplayer.HasMultiplayerPeer()` (EvaPlayer.cs, Phase1Manager.cs)
- `Multiplayer.GetConnectedPeers()` → `Multiplayer.GetPeers()` (Phase1Manager.cs)
- `BoxContainer.Alignment.Center` → `BoxContainer.AlignmentMode.Center` (Phase1Manager.cs)
- Added `<Nullable>enable</Nullable>` to disasteronomy.csproj

### Level Build Fixes (applied)
- `Phase1Manager._Ready` was building level content under bare `Node2D` — `ColorRect` (Control) children don't render under Node2D. Wrapped all world visuals in a `CanvasLayer` with `Layer = -100` so background, stars, airlock, CPS, handrails, and bounds rect are visible.
- Stars now drawn via `_Draw()` with fixed seed instead of 200 individual `ColorRect` nodes.
- `ConfigureSpawner()` now uses `spawner.SpawnPath = NodePath(".")` and `spawner.AddSpawnableScene()` instead of `spawner.Set()`.
- `EvaPlayer.tscn` moved from `Scripts/Phase1/` to `Scenes/Phase1/EvaPlayer.tscn` as minimal scene.
- `Phase1.tscn` updated: `MultiplayerSpawner` has `spawn_path = "."` and `network_player` pointing to `Scenes/Phase1/EvaPlayer.tscn`.
- Added `GD.Print` lines: "Phase1: level built", "Phase1: spawner configured with EvaPlayer scene", "Phase1: player spawned at {position}".

---

*Última atualização: 14/11/2026*
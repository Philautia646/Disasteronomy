# Disasteronomy — guia de assets (sons e sprites)

O jogo já procura estes arquivos sozinho. **Não precisa mexer em código**: é só salvar com o nome e na pasta certos. Se o arquivo não existir, o jogo continua com o desenho provisório / sem som.

## Sons — `res://Music/sfx/`
Formatos: `.ogg` (preferido), `.wav` ou `.mp3`. Nome do arquivo = nome da lista (sem espaços, minúsculo).

| Arquivo | Quando toca | Fase |
|---|---|---|
| `safer_burst` | jato de nitrogênio do SAFER | 1 |
| `rail_grab` | braço agarra o corrimão | 1 |
| `valve_close` | válvula fechada | 2 |
| `filter_replace` | filtro de carvão trocado | 2 |
| `radio_incoming` | transmissão chegando de Houston (pode ter estática) | 2 |
| `terminal_key` | tecla digitada no terminal | 2 |
| `terminal_error` | código inválido | 2 |
| `terminal_ok` | código aceito | 2 |
| `oxygen_alarm` | alarme (toca a cada 4 s com oxigênio abaixo de 25%) | 2 |
| `jump` | pulo | 2 |
| `mission_complete` | fase concluída | 1 e 2 |
| `mission_fail` | falha de missão | 1 e 2 |

Dicas: sons curtos (até ~2 s), sem silêncio no começo, volume parecido entre eles.

## Sprites dos personagens (quadros de 32x32)
- PNG com fundo transparente, **personagem virado para a DIREITA** (o jogo espelha sozinho para a esquerda).
- No Godot: criar um recurso **SpriteFrames** e salvar no caminho abaixo, com os nomes de animação exatos (em minúsculo). Se uma animação faltar, o jogo usa `idle` / `float`.
- Se o recurso não existir, o jogo desenha o boneco provisório.

| Personagem | Caminho do recurso | Animações |
|---|---|---|
| Astronauta (Fase 1, gravidade zero) | `res://Sprites/player_eva.tres` | `float`, `grab` |
| Tripulante (Fase 2, plataforma) | `res://Sprites/player_phase2.tres` | `idle`, `walk`, `jump`, `climb` |

O ponto central do sprite fica no meio do corpo (o jogo posiciona pelo centro do quadro).

## Cenário (ainda sem hook automático)
Hoje o cenário usa retângulos de cor (corrimões, CPS, airlock, racks, válvulas, filtros, terminal). Combine o tamanho antes de desenhar para eu ligar depois:
- Tiles/blocos em múltiplos de 32 px.
- Itens interativos (válvula, filtro, terminal): sprites de 32x32.
- Fundo: uma imagem grande ou camadas de estrelas / interior do lab.

## Já combinado
- Pixel art sem suavização (o projeto já usa filtro "nearest").
- Resolução-base 32x32 para personagens.
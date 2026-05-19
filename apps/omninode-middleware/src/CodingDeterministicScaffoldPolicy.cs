using System.Net;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class CodingDeterministicScaffoldPolicy
{
    private static readonly Regex DomainRegex = new(@"([a-z0-9][a-z0-9-]*\.[a-z]{2,})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryGenerateUiCloneScaffold(
        string objective,
        out IReadOnlyList<ScaffoldFileSpec> files
    )
    {
        files = Array.Empty<ScaffoldFileSpec>();
        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        var explicitLanguage = CodingLanguagePolicy.ResolveExplicitObjectiveLanguage(objective);
        if (!string.IsNullOrWhiteSpace(explicitLanguage)
            && explicitLanguage is not ("html" or "css" or "javascript"))
        {
            return false;
        }

        var isUiClone = ContainsAny(
            text,
            "클론",
            "clone",
            "ui",
            "레이아웃",
            "html",
            "css",
            "landing",
            "페이지"
        );
        if (!isUiClone || ContainsAny(text, "게임", "game", "테트리스", "tetris", "슈팅", "shooter", "비행기", "1942"))
        {
            return false;
        }

        var domainMatch = DomainRegex.Match(text);
        var folder = domainMatch.Success ? CodingExecutionSafetyPolicy.SanitizePathSegment(domainMatch.Groups[1].Value) : "web-clone";
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = "web-clone";
        }

        var indexPath = $"{folder}/index.html";
        var cssPath = $"{folder}/styles.css";
        var jsPath = $"{folder}/script.js";

        var indexContent = $"""
                            <!doctype html>
                            <html lang="ko">
                            <head>
                              <meta charset="utf-8" />
                              <meta name="viewport" content="width=device-width, initial-scale=1" />
                              <title>{folder} UI Clone</title>
                              <link rel="stylesheet" href="styles.css" />
                            </head>
                            <body>
                              <header class="topbar">
                                <div class="logo">{folder.ToUpperInvariant()}</div>
                                <nav class="menu">
                                  <a href="#">메일</a>
                                  <a href="#">카페</a>
                                  <a href="#">블로그</a>
                                  <a href="#">쇼핑</a>
                                </nav>
                              </header>
                              <main class="layout">
                                <section class="hero">
                                  <h1>{folder} 클론 UI</h1>
                                  <p>요청에 따라 실제 기능은 제외하고 화면만 구현된 버전입니다.</p>
                                </section>
                                <section class="grid">
                                  <article class="card">콘텐츠 카드 1</article>
                                  <article class="card">콘텐츠 카드 2</article>
                                  <article class="card">콘텐츠 카드 3</article>
                                </section>
                              </main>
                              <script src="script.js"></script>
                            </body>
                            </html>
                            """;
        var cssContent = """
                         * { box-sizing: border-box; }
                         body { margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Noto Sans KR', sans-serif; background: #f4f6f8; color: #1f2937; }
                         .topbar { height: 64px; display: flex; align-items: center; justify-content: space-between; padding: 0 20px; background: #ffffff; border-bottom: 1px solid #e5e7eb; position: sticky; top: 0; }
                         .logo { font-weight: 800; color: #03c75a; letter-spacing: 0.04em; }
                         .menu { display: flex; gap: 16px; }
                         .menu a { text-decoration: none; color: #374151; font-size: 14px; }
                         .layout { max-width: 1080px; margin: 24px auto; padding: 0 16px; }
                         .hero { background: #ffffff; border: 1px solid #e5e7eb; border-radius: 12px; padding: 24px; margin-bottom: 16px; }
                         .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }
                         .card { background: #ffffff; border: 1px solid #e5e7eb; border-radius: 10px; min-height: 140px; padding: 16px; }
                         """;
        var jsContent = """
                        document.addEventListener('DOMContentLoaded', () => {
                          console.log('UI clone rendered.');
                        });
                        """;

        files = new[]
        {
            new ScaffoldFileSpec(indexPath, indexContent),
            new ScaffoldFileSpec(cssPath, cssContent),
            new ScaffoldFileSpec(jsPath, jsContent)
        };
        return true;
    }

    public static bool TryGenerateWebShooterScaffold(
        string objective,
        string languageHint,
        out IReadOnlyList<ScaffoldFileSpec> files
    )
    {
        files = Array.Empty<ScaffoldFileSpec>();
        var lang = CodingLanguagePolicy.NormalizeCodingLanguageHintPreservingAuto(languageHint);
        var explicitLanguage = CodingLanguagePolicy.ResolveExplicitObjectiveLanguage(objective);
        if (!string.IsNullOrWhiteSpace(explicitLanguage)
            && explicitLanguage is not ("html" or "javascript" or "css"))
        {
            return false;
        }

        if (lang != "auto" && lang != "html" && lang != "javascript" && lang != "css")
        {
            return false;
        }

        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var webSignals = ContainsAny(text, "웹", "web", "브라우저", "html", "canvas");
        var shooterSignals = ContainsAny(text, "슈팅", "shooter", "shooting", "비행기", "fighter", "flight", "arcade", "종스크롤", "scroll");
        var gameSignals = ContainsAny(text, "게임", "game");
        var frontendRequested = lang is "html" or "javascript" or "css"
            || explicitLanguage is "html" or "javascript" or "css";
        if (!(shooterSignals && gameSignals && (webSignals || frontendRequested)))
        {
            return false;
        }

        var indexContent = """
                           <!doctype html>
                           <html lang="ko">
                           <head>
                             <meta charset="utf-8" />
                             <meta name="viewport" content="width=device-width, initial-scale=1" />
                             <title>Sky Patrol</title>
                             <style>
                               :root {
                                 color-scheme: dark;
                                 --bg-top: #07111f;
                                 --bg-bottom: #10345a;
                                 --panel: rgba(7, 15, 28, 0.82);
                                 --line: rgba(255, 255, 255, 0.12);
                                 --text: #f4f7fb;
                                 --accent: #ffd166;
                                 --danger: #ff5d5d;
                                 --ok: #67e8b1;
                               }
                               * { box-sizing: border-box; }
                               body {
                                 margin: 0;
                                 min-height: 100vh;
                                 display: grid;
                                 place-items: center;
                                 background:
                                   radial-gradient(circle at top, rgba(255,255,255,0.08), transparent 30%),
                                   linear-gradient(180deg, var(--bg-top), var(--bg-bottom));
                                 color: var(--text);
                                 font-family: "Trebuchet MS", "Noto Sans KR", sans-serif;
                               }
                               .shell {
                                 width: min(100vw, 1024px);
                                 padding: 16px;
                               }
                               .hud {
                                 display: flex;
                                 justify-content: space-between;
                                 gap: 12px;
                                 padding: 12px 14px;
                                 margin-bottom: 12px;
                                 border: 1px solid var(--line);
                                 border-radius: 16px;
                                 background: var(--panel);
                                 backdrop-filter: blur(10px);
                                 text-transform: uppercase;
                                 letter-spacing: 0.08em;
                                 font-size: 13px;
                               }
                               .hud strong {
                                 color: var(--accent);
                                 font-size: 18px;
                                 margin-left: 8px;
                               }
                               .frame {
                                 position: relative;
                                 border-radius: 18px;
                                 overflow: hidden;
                                 border: 1px solid rgba(255,255,255,0.14);
                                 box-shadow: 0 28px 80px rgba(0, 0, 0, 0.35);
                               }
                               canvas {
                                 display: block;
                                 width: 100%;
                                 height: auto;
                                 background: linear-gradient(180deg, rgba(7,17,31,0.98), rgba(11,31,54,0.98));
                               }
                               .overlay {
                                 position: absolute;
                                 inset: 0;
                                 display: grid;
                                 place-items: center;
                                 background: linear-gradient(180deg, rgba(4, 10, 18, 0.25), rgba(4, 10, 18, 0.82));
                                 text-align: center;
                                 padding: 24px;
                               }
                               .panel {
                                 width: min(92%, 520px);
                                 padding: 28px 24px;
                                 border-radius: 20px;
                                 border: 1px solid var(--line);
                                 background: rgba(5, 12, 22, 0.88);
                                 box-shadow: 0 24px 70px rgba(0, 0, 0, 0.28);
                               }
                               .eyebrow {
                                 color: var(--ok);
                                 letter-spacing: 0.3em;
                                 font-size: 12px;
                                 text-transform: uppercase;
                               }
                               h1 {
                                 margin: 12px 0 8px;
                                 font-size: clamp(34px, 6vw, 58px);
                                 line-height: 0.95;
                               }
                               p {
                                 margin: 0;
                                 color: rgba(244, 247, 251, 0.82);
                                 line-height: 1.6;
                               }
                               .controls {
                                 margin-top: 18px;
                                 padding-top: 18px;
                                 border-top: 1px solid var(--line);
                                 display: grid;
                                 gap: 6px;
                                 font-size: 14px;
                               }
                               .cta {
                                 margin-top: 20px;
                                 display: inline-flex;
                                 align-items: center;
                                 justify-content: center;
                                 min-width: 180px;
                                 min-height: 48px;
                                 padding: 0 18px;
                                 border-radius: 999px;
                                 border: 1px solid rgba(255, 209, 102, 0.4);
                                 background: linear-gradient(180deg, rgba(255, 209, 102, 0.28), rgba(255, 209, 102, 0.1));
                                 color: #fff7df;
                                 font-weight: 700;
                               }
                               .hidden { display: none; }
                               @media (max-width: 640px) {
                                 .hud {
                                   flex-wrap: wrap;
                                   font-size: 12px;
                                 }
                               }
                             </style>
                           </head>
                           <body>
                             <div class="shell">
                               <div class="hud">
                                 <div>Score <strong id="score">0</strong></div>
                                 <div>Lives <strong id="lives">3</strong></div>
                                 <div>Wave <strong id="wave">1</strong></div>
                               </div>
                               <div class="frame">
                                 <canvas id="game" width="960" height="640"></canvas>
                                 <div id="overlay" class="overlay">
                                   <div class="panel">
                                     <div class="eyebrow">Arcade Shooter</div>
                                     <h1>Sky Patrol</h1>
                                     <p>브라우저에서 바로 실행되는 종스크롤 아케이드 슈팅 게임입니다. 적 편대를 피하고 격추해 최고 점수를 노리세요.</p>
                                     <div class="controls">
                                       <div>이동: 화살표 또는 WASD</div>
                                       <div>사격: Space 또는 J</div>
                                       <div>시작/재시작: Enter</div>
                                     </div>
                                     <div class="cta" id="overlayButton">Enter 키로 출격</div>
                                   </div>
                                 </div>
                               </div>
                             </div>
                             <script>
                               const canvas = document.getElementById("game");
                               const ctx = canvas.getContext("2d");
                               const overlay = document.getElementById("overlay");
                               const overlayButton = document.getElementById("overlayButton");
                               const scoreEl = document.getElementById("score");
                               const livesEl = document.getElementById("lives");
                               const waveEl = document.getElementById("wave");

                               const state = {
                                 running: false,
                                 gameOver: false,
                                 score: 0,
                                 wave: 1,
                                 spawnTimer: 0,
                                 stars: Array.from({ length: 90 }, () => ({
                                   x: Math.random() * canvas.width,
                                   y: Math.random() * canvas.height,
                                   size: Math.random() * 2 + 1,
                                   speed: Math.random() * 90 + 25
                                 })),
                                 player: null,
                                 bullets: [],
                                 enemyBullets: [],
                                 enemies: [],
                                 particles: [],
                                 keys: new Set(),
                                 lastTick: 0
                               };

                               function resetGame() {
                                 state.running = true;
                                 state.gameOver = false;
                                 state.score = 0;
                                 state.wave = 1;
                                 state.spawnTimer = 0;
                                 state.bullets = [];
                                 state.enemyBullets = [];
                                 state.enemies = [];
                                 state.particles = [];
                                 state.player = {
                                   x: canvas.width / 2 - 24,
                                   y: canvas.height - 92,
                                   w: 48,
                                   h: 54,
                                   speed: 340,
                                   cooldown: 0,
                                   lives: 3,
                                   fireRate: 0.16
                                 };
                                 syncHud();
                                 overlay.classList.add("hidden");
                               }

                               function syncHud() {
                                 scoreEl.textContent = String(state.score);
                                 livesEl.textContent = String(Math.max(0, state.player ? state.player.lives : 0));
                                 waveEl.textContent = String(state.wave);
                               }

                               function setOverlay(title, message, action) {
                                 overlay.classList.remove("hidden");
                                 overlay.querySelector("h1").textContent = title;
                                 overlay.querySelector("p").textContent = message;
                                 overlayButton.textContent = action;
                               }

                               function spawnEnemy() {
                                 const width = 34 + Math.random() * 18;
                                 const type = Math.random() > 0.72 ? "ace" : "scout";
                                 const speed = type === "ace" ? 130 + state.wave * 10 : 90 + state.wave * 8;
                                 state.enemies.push({
                                   x: 40 + Math.random() * (canvas.width - 80 - width),
                                   y: -70,
                                   w: width,
                                   h: type === "ace" ? 44 : 34,
                                   speed,
                                   hp: type === "ace" ? 3 : 1,
                                   fireTimer: 0.8 + Math.random() * 1.8,
                                   drift: (Math.random() * 2 - 1) * (type === "ace" ? 70 : 35),
                                   seed: Math.random() * Math.PI * 2,
                                   type
                                 });
                               }

                               function firePlayerBullet() {
                                 const p = state.player;
                                 state.bullets.push(
                                   { x: p.x + 9, y: p.y - 6, w: 8, h: 20, speed: 520 },
                                   { x: p.x + p.w - 17, y: p.y - 6, w: 8, h: 20, speed: 520 }
                                 );
                               }

                               function fireEnemyBullet(enemy) {
                                 state.enemyBullets.push({
                                   x: enemy.x + enemy.w / 2 - 3,
                                   y: enemy.y + enemy.h,
                                   w: 6,
                                   h: 16,
                                   speed: 220 + state.wave * 10
                                 });
                               }

                               function explode(x, y, color) {
                                 for (let i = 0; i < 18; i += 1) {
                                   const angle = (Math.PI * 2 * i) / 18;
                                   const speed = 60 + Math.random() * 140;
                                   state.particles.push({
                                     x,
                                     y,
                                     vx: Math.cos(angle) * speed,
                                     vy: Math.sin(angle) * speed,
                                     life: 0.55 + Math.random() * 0.35,
                                     maxLife: 0.9,
                                     size: 2 + Math.random() * 3,
                                     color
                                   });
                                 }
                               }

                               function hitPlayer() {
                                 if (!state.player || state.gameOver) {
                                   return;
                                 }
                                 state.player.lives -= 1;
                                 explode(state.player.x + state.player.w / 2, state.player.y + state.player.h / 2, "#ff9f68");
                                 syncHud();
                                 if (state.player.lives <= 0) {
                                   state.running = false;
                                   state.gameOver = true;
                                   setOverlay("Mission Failed", `최종 점수 ${state.score}점. Enter 키로 다시 출격하세요.`, "Enter 키로 재도전");
                                   return;
                                 }
                                 state.player.x = canvas.width / 2 - state.player.w / 2;
                                 state.player.y = canvas.height - 92;
                               }

                               function rectsIntersect(a, b) {
                                 return a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
                               }

                               function update(dt) {
                                 state.stars.forEach((star) => {
                                   star.y += star.speed * dt;
                                   if (star.y > canvas.height + 4) {
                                     star.y = -4;
                                     star.x = Math.random() * canvas.width;
                                   }
                                 });

                                 state.particles = state.particles.filter((particle) => {
                                   particle.life -= dt;
                                   particle.x += particle.vx * dt;
                                   particle.y += particle.vy * dt;
                                   particle.vx *= 0.98;
                                   particle.vy *= 0.98;
                                   return particle.life > 0;
                                 });

                                 if (!state.running || !state.player) {
                                   return;
                                 }

                                 const p = state.player;
                                 if (state.keys.has("ArrowLeft") || state.keys.has("a")) p.x -= p.speed * dt;
                                 if (state.keys.has("ArrowRight") || state.keys.has("d")) p.x += p.speed * dt;
                                 if (state.keys.has("ArrowUp") || state.keys.has("w")) p.y -= p.speed * dt;
                                 if (state.keys.has("ArrowDown") || state.keys.has("s")) p.y += p.speed * dt;
                                 p.x = Math.max(18, Math.min(canvas.width - p.w - 18, p.x));
                                 p.y = Math.max(24, Math.min(canvas.height - p.h - 16, p.y));

                                 p.cooldown -= dt;
                                 if ((state.keys.has(" ") || state.keys.has("j")) && p.cooldown <= 0) {
                                   firePlayerBullet();
                                   p.cooldown = p.fireRate;
                                 }

                                 state.spawnTimer -= dt;
                                 if (state.spawnTimer <= 0) {
                                   spawnEnemy();
                                   const density = Math.max(0.28, 1.05 - state.wave * 0.05);
                                   state.spawnTimer = density;
                                 }

                                 state.bullets.forEach((bullet) => { bullet.y -= bullet.speed * dt; });
                                 state.enemyBullets.forEach((bullet) => { bullet.y += bullet.speed * dt; });
                                 state.bullets = state.bullets.filter((bullet) => bullet.y + bullet.h > -10);
                                 state.enemyBullets = state.enemyBullets.filter((bullet) => bullet.y < canvas.height + 20);

                                 for (const enemy of state.enemies) {
                                   enemy.y += enemy.speed * dt;
                                   enemy.x += Math.sin((performance.now() / 1000) * 2.4 + enemy.seed) * enemy.drift * dt;
                                   enemy.x = Math.max(10, Math.min(canvas.width - enemy.w - 10, enemy.x));
                                   enemy.fireTimer -= dt;
                                   if (enemy.fireTimer <= 0) {
                                     fireEnemyBullet(enemy);
                                     enemy.fireTimer = enemy.type === "ace" ? 0.75 : 1.6 + Math.random() * 0.8;
                                   }
                                 }

                                 for (let i = state.enemies.length - 1; i >= 0; i -= 1) {
                                   const enemy = state.enemies[i];
                                   if (enemy.y > canvas.height + 30) {
                                     state.enemies.splice(i, 1);
                                     continue;
                                   }

                                   if (rectsIntersect(enemy, p)) {
                                     state.enemies.splice(i, 1);
                                     hitPlayer();
                                   }
                                 }

                                 for (let i = state.bullets.length - 1; i >= 0; i -= 1) {
                                   const bullet = state.bullets[i];
                                   let consumed = false;
                                   for (let j = state.enemies.length - 1; j >= 0; j -= 1) {
                                     const enemy = state.enemies[j];
                                     if (!rectsIntersect(bullet, enemy)) {
                                       continue;
                                     }
                                     enemy.hp -= 1;
                                     consumed = true;
                                     if (enemy.hp <= 0) {
                                       state.enemies.splice(j, 1);
                                       state.score += enemy.type === "ace" ? 180 : 60;
                                       if (state.score > 0 && state.score % 900 === 0) {
                                         state.wave += 1;
                                       }
                                       explode(enemy.x + enemy.w / 2, enemy.y + enemy.h / 2, enemy.type === "ace" ? "#ffd166" : "#9ad1ff");
                                       syncHud();
                                     }
                                     break;
                                   }
                                   if (consumed) {
                                     state.bullets.splice(i, 1);
                                   }
                                 }

                                 for (let i = state.enemyBullets.length - 1; i >= 0; i -= 1) {
                                   if (rectsIntersect(state.enemyBullets[i], p)) {
                                     state.enemyBullets.splice(i, 1);
                                     hitPlayer();
                                   }
                                 }
                               }

                               function drawBackground() {
                                 const sky = ctx.createLinearGradient(0, 0, 0, canvas.height);
                                 sky.addColorStop(0, "#07111f");
                                 sky.addColorStop(1, "#0d3153");
                                 ctx.fillStyle = sky;
                                 ctx.fillRect(0, 0, canvas.width, canvas.height);

                                 state.stars.forEach((star) => {
                                   ctx.fillStyle = `rgba(255,255,255,${0.35 + star.size * 0.12})`;
                                   ctx.fillRect(star.x, star.y, star.size, star.size * 1.6);
                                 });
                               }

                               function drawPlayer(player) {
                                 ctx.save();
                                 ctx.translate(player.x, player.y);
                                 ctx.fillStyle = "#d9f5ff";
                                 ctx.beginPath();
                                 ctx.moveTo(player.w / 2, 0);
                                 ctx.lineTo(player.w, player.h - 8);
                                 ctx.lineTo(player.w / 2 + 8, player.h - 12);
                                 ctx.lineTo(player.w / 2 + 3, player.h);
                                 ctx.lineTo(player.w / 2 - 3, player.h);
                                 ctx.lineTo(player.w / 2 - 8, player.h - 12);
                                 ctx.lineTo(0, player.h - 8);
                                 ctx.closePath();
                                 ctx.fill();
                                 ctx.fillStyle = "#ff6b6b";
                                 ctx.fillRect(player.w / 2 - 4, 10, 8, 18);
                                 ctx.restore();
                               }

                               function drawEnemy(enemy) {
                                 ctx.save();
                                 ctx.translate(enemy.x, enemy.y);
                                 ctx.fillStyle = enemy.type === "ace" ? "#ff9d5c" : "#ff5d7a";
                                 ctx.beginPath();
                                 ctx.moveTo(enemy.w / 2, enemy.h);
                                 ctx.lineTo(enemy.w, enemy.h * 0.2);
                                 ctx.lineTo(enemy.w * 0.68, 0);
                                 ctx.lineTo(enemy.w * 0.5, enemy.h * 0.28);
                                 ctx.lineTo(enemy.w * 0.32, 0);
                                 ctx.lineTo(0, enemy.h * 0.2);
                                 ctx.closePath();
                                 ctx.fill();
                                 ctx.fillStyle = "rgba(255,255,255,0.5)";
                                 ctx.fillRect(enemy.w / 2 - 3, enemy.h * 0.24, 6, 12);
                                 ctx.restore();
                               }

                               function drawProjectiles() {
                                 ctx.fillStyle = "#ffe08a";
                                 state.bullets.forEach((bullet) => ctx.fillRect(bullet.x, bullet.y, bullet.w, bullet.h));
                                 ctx.fillStyle = "#ff8269";
                                 state.enemyBullets.forEach((bullet) => ctx.fillRect(bullet.x, bullet.y, bullet.w, bullet.h));
                               }

                               function drawParticles() {
                                 state.particles.forEach((particle) => {
                                   const alpha = Math.max(0, particle.life / particle.maxLife);
                                   ctx.fillStyle = particle.color.replace(")", `, ${alpha})`).replace("rgb", "rgba");
                                   if (!ctx.fillStyle.includes("rgba")) {
                                     ctx.fillStyle = particle.color;
                                     ctx.globalAlpha = alpha;
                                   } else {
                                     ctx.globalAlpha = 1;
                                   }
                                   ctx.fillRect(particle.x, particle.y, particle.size, particle.size);
                                 });
                                 ctx.globalAlpha = 1;
                               }

                               function draw() {
                                 drawBackground();
                                 drawProjectiles();
                                 state.enemies.forEach(drawEnemy);
                                 if (state.player) {
                                   drawPlayer(state.player);
                                 }
                                 drawParticles();
                               }

                               function loop(timestamp) {
                                 if (!state.lastTick) {
                                   state.lastTick = timestamp;
                                 }
                                 const dt = Math.min(0.033, (timestamp - state.lastTick) / 1000);
                                 state.lastTick = timestamp;
                                 update(dt);
                                 draw();
                                 requestAnimationFrame(loop);
                               }

                               window.addEventListener("keydown", (event) => {
                                 const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;
                                 if (key === "Enter") {
                                   resetGame();
                                   return;
                                 }
                                 if (key === " ") {
                                   event.preventDefault();
                                 }
                                 state.keys.add(key);
                               });

                               window.addEventListener("keyup", (event) => {
                                 const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;
                                 state.keys.delete(key);
                               });

                               setOverlay("Sky Patrol", "Enter 키를 눌러 출격하세요. 적 편대를 격추하고 생존 시간을 늘리세요.", "Enter 키로 출격");
                               syncHud();
                               requestAnimationFrame(loop);
                             </script>
                           </body>
                           </html>
                           """;

        files = new[]
        {
            new ScaffoldFileSpec("index.html", indexContent)
        };
        return true;
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}

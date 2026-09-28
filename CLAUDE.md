# MuseXR

**ACTIVE PLAN: `../chatplan.md`** — the ten-stage journey rebuild. Read it before starting work here.
Phase 1 in progress; **327/327 EditMode tests green**. The scene is `Assets/Scenes/Museum.unity`,
and it now RUNS: `MuseumJourneyRunner` walks all ten stages, loads a chapter world and enables
walking in stage 04. Dialogue is not yet wired into the gallery.

**Splat quality: `QUALITY.md`** (branch `quality-benchmark`, 28 Sep 2026) — why the worlds look
the way they do on Quest, the headset limits (nothing above ~3.3M splats renders; 4.32M draws
nothing), the unhandled anti-aliasing flag, Chisel as the way to make dense rooms, and which
reduction methods fail. Web research behind it: `Tools/splat/RESEARCH-quest-quality.md`. Frame
rate lives in `PERFORMANCE.md`. Read QUALITY.md before touching splat assets or the renderer.

**Worklist:** https://claude.ai/code/artifact/fda7d729-06cd-46e5-98bb-4453e30929ed
Read it at the start of every session — open tasks and the questions waiting on Saul live there.

**Project context:** `../CLAUDE.md` (one level up, outside any repo). Note this is the
**Quest / PICO 4 hardware** project, on Unity 6000.3.12f1 — NOT the emulator one. It has been
dormant since the emulator road began. Its sibling `MusePico/` is on an older Unity, so content
moves between them by reconverting from source, never by copying serialized assets.

<!-- pico-cli:plugin-context:pico-unity-agentic-tools:start -->
@./PICO-UNITY-AGENTIC-TOOLS.AGENTS.md
<!-- pico-cli:plugin-context:pico-unity-agentic-tools:end -->

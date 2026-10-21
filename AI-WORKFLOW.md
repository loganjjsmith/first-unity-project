# How this was built with AI agents

Before this project, I had never touched Unity (beyond playing games made with it) or even made a game. I'm an Enterprise Java developer, and I wanted to find out two things: 
1) How fast I could learn a new field after working in a steady job and field for over a decade and 
2) How to work well with an AI coding agent rather than just prompting one. 

This page describes how that went, including the parts that went wrong.

## Who did what

**Two agents, in sequence.** The project started in July 2026 with **Grok**, when I wanted to stretch my legs and prepare for the upcoming career transition. I experienced some issues with thrashing and productivity with Grok, and my promo rate for the service ran out, so in September I decided to try **Claude Code** (Anthropic). Claude Code started by reading everything Grok had left behind, then reviewed the codebase before changing anything. (We unfortunately had to scrap a large portion of the POC and do a large rebuild)

**The agents** wrote most of the C#: the movement maths (which, upon seeing 3D spatial code for the first time made me realize the benefits of AI, otherwise I would have had to have taken classes to even get this far), the mode state machine, the ragdoll and slingshot systems, and the unit tests. They also did the research, the code
reviews and the step-by-step Editor checklists I worked from. (I made sure the Agent rules were that of a senior game dev training an intern)

**I** directed the project and made every design and scope decision. I also:

- did the Unity Editor work: building the ragdoll rig by hand (capsule colliders and 11
  `CharacterJoint`s), laying out the poles and wires, building the animator controller and the
  climbing IK rig, and setting up materials, the player prefab and physics settings;
- wrote the small pieces of code where the learning was: the paw-placement raycast and step arc for
  climbing, the camera "up" rotation, the velocity tracker, the air-steer maths and the launch spin;
- playtested every feature and made the feel calls, and caught bugs the agents missed (sprint
  running slower than walking; the ragdoll bones not matching the model);
- set the working rules described below, mostly in response to things going wrong.

The split was deliberate. Small, conceptually new code (vectors, quaternions, curves) I wrote myself
with coaching, because that is where the learning happens. Large code that was not a solved problem,
the agent wrote. Anything generic (character controller, camera, splines, IK) came from Unity packages
rather than custom code.

## The working rules, and why each exists

Each agent read a project rules file at the start of every session. The rules that mattered most:

- **Options before code.** Before any new system, research Unity built-ins and free assets, present real
  options, and let me choose. Custom code is the last resort.
- **Three attempts, then stop.** On the same bug with the same approach, the agent gets three tries.
  Breaking something that already worked counts as a try. After that it stops coding, lists options again
  (including "undo and do it properly"), and waits for me.
- **Protect what works.** Before touching the player, camera or movement, the plan lists every
  feature that currently works and must still work afterwards.
- **Verify, then report.** Nothing is "done" until it has at least compiled, and the agent says exactly
  what it ran. "Typecheck clean, tests not run" is a good report; "verified" on its own is not.
- **Reproduce before fixing.** Get evidence from the running game first, then fix. No guessing.
- **The agent never pushes.** It commits only after my thumbs-up, and three local guard layers (a
  permissions deny-list, a pre-command hook, and a git `reference-transaction` hook) stop it writing to
  the protected branches. The hook and settings are in [`.claude/`](.claude/).

## What the AI got wrong, and how it was caught

These are real incidents from the project's notes, lightly condensed.

### 1. Grok built everything custom, and the code became unmaintainable (Grok)

The rules already said "research packages first", but Grok defaulted to hand-written code for things that
are solved problems, even down to WASD moving the character. Every feature cost several rounds of debugging, and the player
controller grew into a single 91 KB script. 

**How it was caught:** I noticed the cost per feature climbing, and the first thing I asked Claude Code for was a review of Grok's code against the project's own rules.

**What changed:** a package-first rebuild. Unity's Starter Assets controller, Cinemachine, Splines and
Animation Rigging replaced the custom stack, the god script was retired, and pure logic moved into a
separately tested assembly. I also added a rule against god scripts *and* against the opposite extreme of
splitting code into dozens of tiny files.

### 2. Grok thrashed on the ragdoll and broke working features (Grok)

While iterating on a multi-limb ragdoll, each attempt changed something else: camera, movement and launch
all broke at various points, which meant zero or negative progress. 

**How it was caught:** playtesting.
Features that had worked the previous day no longer did. 

**What changed:** I stopped the work and wrote the
three-attempt rule and the "protect what works" rule above. Regressions count as failed attempts.

### 3. Four wrong guesses before one real diagnosis (Claude Code)

During the rebuild the camera seemed to "float like a free-cam". Claude Code tried four fixes and
explanations in a row, one of them ("the camera clipped inside a hill") presented as a finding. The ground was flat.

**How it was caught:** connecting the agent to the running Unity Editor and logging positions frame by
frame. After 1.5 seconds of walking, the visible body was 84 units behind the invisible player root. The
real cause was in the agent's own code from the day before: interpolation switched on for rigidbodies that
were kinematic and being carried along by their parent. In the same session it claimed a launch had
"landed intact". A per-frame trace showed the limb colliders were all disabled and the body had fallen
through the floor. 

**What changed:** the "reproduce before fixing" and "verify, then report" rules, and the
habit of tracing frame by frame instead of reasoning from a screenshot.

### 4. Teaching an older Unity than the one installed (Claude Code)

Claude Code gave me ProBuilder instructions taken from research notes that described an older workflow.
The keyboard shortcut it taught did nothing like what it described in Unity 6. 

**How it was caught:** I
followed the steps and they did not match my screen, so I worked around it with plain primitives and
flagged it. 

**What changed:** a rule to check the installed Editor and package versions before coaching,
and to say plainly when a step has not been checked against the live Editor.

### 5. A licence question with no clean answer (Claude Code, while preparing this repo)

The character controller is built on Unity's free Starter Assets. The Asset Store page lists it under the
Unity Companion License, but Unity's own support article says Starter Assets stay under the Asset Store
EULA and cannot be redistributed. Rather than guess, we chose the conservative reading: the Unity files
are not in this repo. Our changes to them ship as a patch you apply after importing the package from the
Asset Store, much like declaring a dependency and overriding part of it. Setup steps are in the
[README](README.md).

### 6. Robot tests that only passed by accident (Claude Code, found while preparing this repo)

Claude Code wrote 14 Play-mode "robot" tests that play the real scene with a virtual keyboard. They
passed 14/14 in the working project. In this cleaned-up copy, only the first test passed. After that,
the virtual key presses stopped reaching the player.

**How it was caught:** moving the tests into a copy without the project's later, unrelated packages.
Instead of guessing, the agent ran one experiment: add those packages back temporarily. All 14 passed,
which proved one of them was changing input behaviour. By default, Unity's Input System only sends
keyboard input to the game while the Game view has focus. A headless test run has no Game view, and one
of the packages had been quietly relaxing that rule.

**What changed:** the test fixture now sets that rule itself. Two settings lines in test code; no game
code changed. The tests pass 14/14 without the extra packages. Found and fixed in two attempts, within
the three-attempt rule.

## What is deliberately not here

This repository is a snapshot of the proof of concept, to show my ability to learn two brand new domains at once (Game development and AI partner coding). The design documents, roadmap and working notes
behind it are not included, because I would rather not reveal what the project may grow into later.
The repository contains the code, scene and unit tests as they were when the proof of concept was finished, apart from some sanitization to allow this particular repo to be made public. The robot tests were written just after the proof of concept and are included too, and the browser page gained a controls and goals panel.

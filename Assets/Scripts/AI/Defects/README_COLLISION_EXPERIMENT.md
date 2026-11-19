# Collision-defect pilot

Open `Assets/Scenes/AI/QA_CollisionExperiment.unity`, or use **Tesis QA → Defect Experiment → Open evaluation scene**. This scene is separate from the Phase3 training scene. No Python trainer is used.

The pinned model is `results/Phase3R/Explorer/Explorer-2000063.onnx`, copied byte-for-byte into `Assets/Experiments/CollisionPilot/Phase3R_Explorer_2000063.onnx`. SHA-256: `a6a6410d44a5bffa810553e1f0baa3b9fe4b699a2c8d73c32eb00c6c32d7a9a2`. Its seven observation tensors remain `[24,34,49,10,32,12,16]` (177 values), plus recurrent state. Terrain, Branch, Distant Terrain and Ramp Memory remain enabled. Long Range replacement and Completed Branch Assist remain off. The environment uses the Phase3R reward revision 1, even if the current training scene uses revision 2. Rewards are not learned during inference.

## Running a participant

1. Open the evaluation scene and press Play. Keep the Game view focused for keyboard input.
2. Choose **Manual**, enter a pseudonymous participant ID, and assign an order from 0–9. Use each order equally often where recruitment allows. Orders are five cyclic rotations and their reverses; every scenario appears equally often in each position over a complete block of ten participants.
3. Press **Mulai sesi**. W/S move forward/back, A/D turn, and Up/Down change camera pitch. There is no jump, strafe, sprint or teleport control: the movement motor is shared with AI.
4. Aim the central `+` at a visible floor within six metres and press **F**, or click **Laporkan lokasi bidikan**. Reports may be correct, incorrect, or duplicates. The participant receives only acknowledgement, with no ground-truth feedback, defect count, seed, coverage, or score.
5. Each trial lasts **150 simulation seconds**. Manual mode always uses time scale 1. After each trial, press **Trial berikutnya**. A manual campaign consists of five trials. Complete a separate practice session before participant data collection; do not use the five study scenarios for practice.

Choose **AI** or **Random** in the same lobby for automatic testing. Each uses three repetitions of all five scenarios and advances automatically. Default interactive time scale is 1; a faster automatic time scale can be set on the controller before Play. Random chooses seeded forward/turn actions every 0.7–1.5 seconds and uses the exact same detector as AI.

## Frozen scenarios

| Scenario | Requested map seed | Actual WFC seed | Full collider | Partial hole | Spawn floor |
|---|---:|---:|---:|---:|---:|
| S1 | 170000001 | 170007920 | 0 | 0 | 0 |
| S2 | 170004100 | 170004100 | 2 | 0 | 1 |
| S3 | 170008199 | 170008199 | 0 | 2 | 0 |
| S4 | 170012298 | 170012298 | 2 | 2 | 1 |
| S5 | 170016397 | 170016397 | 2 | 2 | 0 |

All use 24×24 cells and two floors, including the existing smooth stair collision ramps. Defects alternate between floors; stairs and the spawn neighbourhood are excluded from injection. Seeds are outside the arithmetic sequence used by the configured Phase3R trainer (`33003 + n*7919`), including WFC restart offsets. This checks the configured seed sequence; it does not establish independence from every possible historical/manual run.

The protocol asset stores separate map, defect and spawn seeds, the resolved WFC seed, exact spawn/yaw, all generated module transforms, mesh geometry hashes, and injected defect locations. Each replay regenerates the map and compares its complete manifest with the frozen version; mismatches stop the session. The JSON copies live in `Assets/Experiments/CollisionPilot/Manifests`. Do not change generator/prefabs/protocol, controller settings, or detector thresholds during a study. Create a new versioned protocol and rerun validation if those change. Five scenarios are a pilot set, not a power-analysis justification for a final study.

## Injection and detector

Normal map and collision preparation finishes first, then injection runs. Full injection disables one selected MeshCollider; if that mesh also includes walls, its collision is disabled as well. This is labelled **FullCollider**, not a claim that only its top faces were removed. Partial injection clones collision geometry and subtracts a **2.4 m square vertical column** from triangles, including the slab underside. Clipping preserves triangle pieces outside the hole; the shared visual/prefab mesh is untouched. Centre and four offset probes verify that another collider does not fill the defect. Spawn collision is rechecked. No per-trial training or model migration is needed.

The detector is a plain C# object, not an ML-Agents sensor. It receives no seed labels or injection manifest and does not steer the policy. Every 0.2 simulated seconds it casts a 9×6 forward/downward fan, within six metres, against cached **visual mesh triangles**. The closest visible surface stops the ray, including walls. Only nearly horizontal upward floor faces within 0.2 m of the module's nominal floor height are checked. Stair modules (identified from their existing module names) remain visual occluders but are excluded from inspection: their visual treads intentionally differ from the smooth collision ramp. A short physics ray around the visual floor point must find matching collision within 0.1 m; hitting a different floor cannot satisfy it. A spatial candidate must appear in two consecutive scans before reporting. Empty space with no visual surface is not a defect. Detector version `local-flat-floor-v2` detects local missing flat-floor collision; stairs, ceilings, wall-only defects, arbitrary visual defects and guaranteed complete detection are outside this pilot's scope. Manual aim reporting uses the same floor eligibility rule.

The geometry library is baked from imported meshes in the Editor. It introduces no reference colliders, does not alter physics layers, and does not require Read/Write to be enabled on shared source models. For the actual experiment colliders, it creates readable runtime meshes from the same baked vertices and indices, shared per source mesh: Unity standalone builds otherwise cannot cook these imported meshes at runtime. These are the physical colliders under test, and injection modifies/disables those same colliders. Rendering retains the original imported meshes. Ground-truth labels are read by the **scorer only**, after either a detector or human report. Multiple reports on the same planted defect count once as a true positive. Repeated unmatched reports within one metre on the same surface are duplicates; other unmatched reports count as false positives.

## Duration, falls and observations

The trial budget owns termination. Training success, anti-spin and no-new-area termination do not end an evaluation trial. The original progress/memory observations are maintained; the normalized step observation still uses the **12,000-step Phase3R horizon** although ML-Agents MaxStep is zero in this scene to prevent its automatic reset. The 150-second trial budget is 7,500 physics steps at 0.02 seconds/step, with an observation clock reset after a fall.

A fall is registered below KillHeight, or when the actor drops more than 1.5 m from its last grounded height while falling faster than 7 m/s. It respawns at the original safe spawn in the same defective map. LSTM/navigation episode memory resets for both manual and automatic modes; trial elapsed time, findings and coverage remain. No defect is automatically credited for falling. Respawn teleport distance is excluded from path length. Simulation time is accumulated from executed movement steps; rendering/loading/respawn processing is excluded. Wall time is recorded separately, including in-trial processing. Do not compare accelerated AI wall time directly with real-time human movement time without identifying that difference.

## Results

Every session creates a unique `results/DefectExperiments/<UTC timestamp>_<id>` folder. Timestamps in folder names are UTC; trial timing is elapsed time.

- `session.txt`: participant, mode, order, Unity version, pinned model hash, fixed timestep, time scale and smoke-test flag.
- `runtime-settings.txt`: motor configuration, observation flags, controller dimensions and movement limits, decision period, and preserved policy step horizon.
- `protocol.json`: frozen protocol snapshot.
- `Sx_ry.json`: manifest, raw findings, TP/FP/duplicate labels, first report and first true detection times, trajectory at 0.5-second intervals, falls, path length and coverage. Running trials checkpoint every five simulation seconds; interrupted/error trials remain labelled as such.
- `summary.csv`: one row per trial. Precision is TP/(TP+FP), recall is TP/planted. Undefined values and non-detection times are empty in CSV (`-1` in JSON). S1 has undefined recall.
- `session.done`: `complete` only after the entire campaign; `failed` on a runtime error. Ending Play early preserves interrupted evidence.

Coverage counts unique eligible non-stair floor cells reached while grounded, divided by the pre-injection eligible floor cells (also separated by floor). Detector sightings do not count as physical coverage. This denominator differs from some training metrics; report this definition explicitly.

Aggregate comparable sessions using the standard-library Python script:

```powershell
.venv/Scripts/python.exe Assets/Scripts/AI/Defects/summarize_defect_experiments.py results/DefectExperiments/pilot_ai_final results/DefectExperiments/pilot_random_final --output results/DefectExperiments/pilot_summary
```

The script checks identical manifests across modes, excludes smoke/interrupted/error trials, and reports type-specific recall. It does not invent human participant results or replace missing detections with zero seconds. An automated check of the manual reporting code is only a software test.

## Validation and batch execution

**Tesis QA → Defect Experiment → Validate frozen scenarios** checks pinned model and source-mesh dependency hashes, exact replay, injection verification, healthy centres, preserved visual geometry, clipping, detector confirmations, lower-floor rejection, wall occlusion, scoring and balanced orders. Stop Play before running it. The **Prepare and validate pilot** command creates assets on the first run and validates existing assets thereafter; it refuses to overwrite a different pinned model or silently re-bake an existing protocol.

For an isolated Unity batch project containing the same Assets/Packages/ProjectSettings, run the following entry point without `-quit`:

```text
-executeMethod TesisQA.AI.Editor.DefectExperimentSetup.RunAutomatic
-defectMode AI
-defectOutput <NEW absolute output directory>
```

Use `-defectMode Random` for the baseline. Add `-defectSmokeSeconds 4` for a short integration check, which is explicitly excluded from aggregation. Batch campaigns use time scale 20 and CPU Burst inference. Do not launch a second Unity process against an already-open project. The provided `.ai-eval` copy was used for validation so the main Editor and training scenes remained available.

The research claim supported by this implementation is **RL-guided exploration plus a rule-based local collision detector**, compared with manual testing and random exploration using the same detector. It does not train the neural network to classify defects.

## Windows player

`Builds/CollisionPilot/CollisionPilot.exe` is the standalone testing app when a build has been generated. The provided **Jalankan_Pengujian.cmd** launcher selects windowed D3D11 graphics and opens the same lobby and controls, without the Unity Editor. Its results are written beside the executable under `results/DefectExperiments/`. Keep the entire build folder together, including its `_Data` directory and DLLs. To build a fresh copy, use **Tesis QA → Defect Experiment → Build Windows testing player**; the command refuses an existing executable to preserve previous builds. The launcher is a small wrapper for the generated executable.

The player supports `-defectMode AI`, `-defectAutoStart true`, `-defectOutput <NEW directory>`, `-defectTimeScale 20`, and `-defectSmokeSeconds 4`. A headless software check can additionally use Unity's `-batchmode -nographics -logFile <path>` options. Interactive manual mode keeps time scale 1. Smoke checks are never participant data.

`pilot_ai_01` was a detector-calibration run: it exposed intentional stair/ramp mismatches. Runs ending in `_02` checked the flat-floor scope and exposed a scorer tolerance mismatch at 0.2 m floor trim. They are preserved as calibration evidence; `pilot_random_02` was stopped during calibration. Use **`pilot_ai_final` and `pilot_random_final`** for the final implementation pilot. Its `collision-region-v2` scorer uses the detector's same nominal floor-height band, with 0.01 m numeric tolerance, and treats unmatched reports near true positives as separate false reports. The frozen five-scenario bank is a pilot/development benchmark; reserve a new untouched scenario bank for any final confirmatory evaluation.

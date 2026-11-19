"""177-input Phase 3 fine-tuning; run from project root. Does not launch on import."""
import hashlib
from pathlib import Path
import sys

SOURCE = "results/Phase2E/Explorer/Explorer-7000033.pt"
SOURCE_SHA256 = "fbfa5feaac98e6fa556089f13bdc9960532c5270944358483f78785360d8665f"
SHAPES = [24, 34, 49, 10, 32, 12, 16]
BALANCED_SOURCE = "results/RecoveryBackups/Phase3_before_balanced_6000091/Explorer-6000091.pt"
BALANCED_SHA256 = "5efcf3ad6e799fd7c7e468125347a29c0b53ad45de1e245c6a5d724bc3ba112f"
SPLIT_SOURCE = "results/RecoveryBackups/Phase3R_before_split_2000063/Explorer-2000063.pt"
SPLIT_SHA256 = "cc47d3f9651666b5482ef89ff2209472d554193315a5dcb847d50cba9f30c5fb"


def validate_phase3(options, run, resume=False):
    """Read-only checks before ML-Agents' permissive partial initializer runs."""
    import torch
    import onnx
    from prepare_branch_observation import make_policy
    from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
    from train_phase2_terrain import constant_parameter

    p = options.environment_parameters
    flags = {"completed_branch_assist": 0, "branch_memory_observation": 1,
             "terrain_distant_observation": 1, "ramp_memory_observation": 1,
             "phase3_curriculum": 1, "stair_search_shaping": 0}
    if any(constant_parameter(p, key, -1) != value for key, value in flags.items()):
        raise ValueError("Phase 3 requires the unchanged 177-input pure-PPO observation flags.")
    behavior = options.behaviors["Explorer"]
    revision = constant_parameter(p, "phase3_reward_revision", 0)
    if revision not in (0, 1, 2):
        raise ValueError("Unsupported reward revision.")
    source, digest, source_step = {
        0: (SOURCE, SOURCE_SHA256, 7000033),
        1: (BALANCED_SOURCE, BALANCED_SHA256, 6000091),
        2: (SPLIT_SOURCE, SPLIT_SHA256, 2000063),
    }[revision]
    if not resume and Path(behavior.init_path).resolve() != Path(source).resolve():
        raise ValueError("Use the pinned initialization checkpoint for this reward profile.")
    checkpoint = run / "Explorer/checkpoint.pt" if resume else Path(source)
    if not resume and hashlib.sha256(checkpoint.read_bytes()).hexdigest() != digest:
        raise ValueError("Pinned source hash mismatch; no files changed.")
    export = run / "Explorer.onnx" if resume else checkpoint.with_suffix(".onnx")
    model = onnx.load(str(export)); onnx.checker.check_model(model)
    shapes = [i.type.tensor_type.shape.dim[-1].dim_value for i in model.graph.input
              if i.name.startswith("obs_")]
    if shapes != SHAPES:
        raise ValueError(f"Phase 3 requires {SHAPES}, received {shapes}.")
    state = torch.load(checkpoint, map_location="cpu")
    policy = make_policy(behavior, SHAPES)
    optimizer = TorchPPOOptimizer(policy, behavior)
    # All trainable modules AND optimizer layouts must match; no silent partial load.
    for name, module in {**policy.get_modules(), **optimizer.get_modules()}.items():
        module.load_state_dict(state[name])
    for group in ("Policy", "Optimizer:critic", "Module:Curiosity"):
        if any(not torch.isfinite(v).all() for v in state[group].values()):
            raise ValueError(f"Non-finite checkpoint values in {group}.")
    if not resume and policy.get_current_step() != source_step:
        raise ValueError("Unexpected initialization checkpoint step.")
    print("Phase 3 verified: 177 inputs, all PPO/critic/curiosity modules compatible; "
          f"time scale {options.engine_settings.time_scale:g}x; training budget {behavior.max_steps:g} steps.", flush=True)


if __name__ == "__main__":
    check_only = "--check-only" in sys.argv
    if check_only:
        sys.argv.remove("--check-only")
    from train_phase2_terrain import main
    main(phase3=True, check_only=check_only)

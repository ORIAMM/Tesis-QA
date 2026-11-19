"""Explicit 117 -> 149 observation migration. Never edits a source checkpoint.
Copies actor/critic/LSTM/curiosity exactly, zero-pads new input columns, creates
fresh optimizer state, verifies old/new policy equivalence, exports new ONNX.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path

OLD_SHAPES = [24, 34, 49, 10]
NEW_SHAPES = OLD_SHAPES + [32]


def make_policy(settings, shapes):
    from mlagents_envs.base_env import BehaviorSpec, ObservationSpec, ObservationType, DimensionProperty, ActionSpec
    from mlagents.trainers.policy.torch_policy import TorchPolicy
    from mlagents.trainers.torch_entities.networks import SimpleActor
    specs = [ObservationSpec((n,), (DimensionProperty.NONE,), ObservationType.DEFAULT, f"obs_{i}")
             for i, n in enumerate(shapes)]
    return TorchPolicy(42, BehaviorSpec(specs, ActionSpec.create_continuous(2)),
                       settings.network_settings, SimpleActor, {"conditional_sigma": False, "tanh_squash": False})


def migrate_states(source, template, *, old_size=117, new_size=149,
                   new_processor=4, preserve_step=False):
    import torch
    result, expanded = {}, []
    for module, fresh in template.items():
        if module == "global_step":
            result[module] = copy.deepcopy(source[module] if preserve_step else fresh)
            continue
        if module.startswith("Optimizer:") and module != "Optimizer:critic":
            result[module] = fresh  # Explicit fresh optimizer for the expanded architecture.
            continue
        if module not in source:
            raise ValueError(f"Missing source module {module}; partial random initialization is not allowed.")
        result[module] = {}
        for name, target in fresh.items():
            if name not in source[module]:
                if f".processors.{new_processor}.normalizer." in name:
                    result[module][name] = target
                    continue
                raise ValueError(f"Unexpected new parameter {module}/{name}")
            old = source[module][name]
            if old.shape == target.shape:
                result[module][name] = old.clone()
            elif name.endswith("_body_endoder.seq_layers.0.weight") and old.shape == (256, old_size) and target.shape == (256, new_size):
                result[module][name] = torch.cat([old, torch.zeros((256, new_size - old_size), dtype=old.dtype)], dim=1)
                expanded.append(f"{module}/{name}")
            else:
                raise ValueError(f"Unsupported shape change {module}/{name}: {old.shape} -> {target.shape}")
    if len(expanded) != 3:
        raise ValueError(f"Expected precisely actor/critic/curiosity input expansion, got {expanded}")
    return result, expanded


def prepare(source_path, destination, config):
    import onnx
    import torch
    from mlagents.trainers import learn
    from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
    from mlagents.trainers.model_saver.torch_model_saver import TorchModelSaver
    source_path, destination = Path(source_path).resolve(), Path(destination).resolve()
    root = Path(__file__).resolve().parents[4]
    if not destination.is_relative_to(root / "results/BranchObservationInit"):
        raise ValueError("Destination must be a new folder under results/BranchObservationInit.")
    if destination.exists():
        raise ValueError("Destination exists. Refusing overwrite; use its existing verified migration or a new folder.")
    model = onnx.load(str(source_path.with_suffix(".onnx")))
    schema = [i.type.tensor_type.shape.dim[-1].dim_value for i in model.graph.input if i.name.startswith("obs_")]
    if schema != OLD_SHAPES:
        raise ValueError(f"Source schema {schema}, expected {OLD_SHAPES}")
    original_hash = hashlib.sha256(source_path.read_bytes()).hexdigest()
    source = torch.load(source_path, map_location="cpu")
    settings = learn.parse_command_line([str(config), "--run-id=MigrationValidationOnly"]).behaviors["Explorer"]
    old = make_policy(settings, OLD_SHAPES)
    old.actor.load_state_dict(source["Policy"], strict=True)
    new = make_policy(settings, NEW_SHAPES)
    optimizer = TorchPPOOptimizer(new, settings)
    modules = {**new.get_modules(), **optimizer.get_modules()}
    state, expanded = migrate_states(source, {k: v.state_dict() for k, v in modules.items()})
    for k, module in modules.items():
        if isinstance(module, torch.nn.Module): module.load_state_dict(state[k], strict=True)
        else: module.load_state_dict(state[k])
    # Identical old observations + memory must yield identical deterministic
    # policy encodings even with NONZERO branch observations at initialization.
    torch.manual_seed(54321)
    max_error = 0.0
    with torch.no_grad():
        for batch in (1, 4, 16):
            obs = [torch.randn(batch, n) for n in OLD_SHAPES]
            memory = torch.randn(1, batch, old.actor.memory_size)
            a, ma = old.actor.network_body(obs, memories=memory)
            b, mb = new.actor.network_body(obs + [torch.rand(batch, 32)], memories=memory)
            error = max((a-b).abs().max().item(), (ma-mb).abs().max().item())
            max_error = max(max_error, error)
            if error > 1e-5: raise ValueError(f"Policy preservation failed: {error}")
    destination.mkdir(parents=True)
    saver = TorchModelSaver(settings, str(destination))
    saver.register(new); saver.register(optimizer)
    saver.save_checkpoint("Explorer", 0)
    exported = onnx.load(str(destination / "Explorer-0.onnx")); onnx.checker.check_model(exported)
    actual = [i.type.tensor_type.shape.dim[-1].dim_value for i in exported.graph.input if i.name.startswith("obs_")]
    if actual != NEW_SHAPES: raise ValueError(f"Unexpected exported schema {actual}")
    if hashlib.sha256(source_path.read_bytes()).hexdigest() != original_hash: raise ValueError("Source changed unexpectedly.")
    report = {"source": str(source_path), "source_sha256": original_hash,
              "source_step": int(source["global_step"]["_GlobalSteps__global_step"].item()),
              "old_shapes": OLD_SHAPES, "new_shapes": NEW_SHAPES, "new_step": 0,
              "expanded": expanded, "optimizer": "reset", "new_input_weights": "zero",
              "max_policy_encoding_error": max_error}
    (destination / "migration.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", default="results/Phase2B/Explorer/Explorer-2000101.pt")
    parser.add_argument("--destination", default="results/BranchObservationInit/Phase2C")
    parser.add_argument("--config", default="Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_BranchObservation.yaml")
    args = parser.parse_args()
    prepare(args.source, args.destination, args.config)

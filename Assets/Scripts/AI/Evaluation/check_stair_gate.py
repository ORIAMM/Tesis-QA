"""Approve a stopped run's CURRENT ONNX before creating a coverage-resume YAML.

uv run python Assets/Scripts/AI/Evaluation/check_stair_gate.py --report <report.json> --run-id Phase2M4
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import re
import sys
from datetime import datetime, timezone

import yaml

FIXED_SEEDS = {913701, 921620, 929539, 937458, 945377}


def validate_report(report, expected_hash=None):
    errors = []
    episodes = report.get("episodes", [])
    if report.get("error"):
        errors.append("Evaluation contains a simulation/inference error.")
    if len(episodes) < 50 or len(episodes) != report.get("requestedEpisodes"):
        errors.append("A complete evaluation with at least 50 episodes is required.")
    if report.get("protocol") != "Phase2-transfer-mixed-v2":
        errors.append("Use the current fixed-seed mixed-distance evaluation protocol.")
    if (report.get("spawnSeed"), report.get("mapSeedBase"), report.get("inferenceSeed"),
            report.get("mapRegenerationInterval"), report.get("deterministicInference")) != (27421, 913701, 58231, 10, False):
        errors.append("Evaluation seeds/settings differ from the fixed protocol.")
    model_hash = report.get("modelSha256", "").lower()
    if not re.fullmatch(r"[a-f0-9]{64}", model_hash):
        errors.append("Model hash missing: rerun evaluation with the updated evaluator.")
    if expected_hash and model_hash != expected_hash.lower():
        errors.append("The evaluated model is not the run's current exported ONNX; stop and evaluate the current checkpoint.")
    if not FIXED_SEEDS.issubset({e.get("mapSeed") for e in episodes}):
        errors.append("Evaluation must include all five fixed WFC map seeds.")
    results = [e.get("result", {}) for e in episodes]
    if any(r.get("lesson") != 3 or r.get("direction") not in (-1, 1) for r in results):
        errors.append("All episodes must test mixed-distance transfer (AI/Lesson 3).")
    if any(r.get("searchShapingActive", False) for r in results):
        errors.append("Gate evaluation must run with training-only route shaping disabled.")
    distances = [r.get("spawnStairSteps", -1) for r in results]
    if (any(d < 0 or d > 4 for d in distances)
            or sum(0 <= d <= 2 for d in distances) < 10 or sum(2 < d <= 4 for d in distances) < 10):
        errors.append("Include at least ten near and ten far spawn episodes.")
    summary = {}
    for name, direction, minimum in (("ascent", 1, 30), ("descent", -1, 10)):
        selected = [r for r in results if r.get("direction") == direction]
        count = len(selected)
        successes = sum(r.get("success") is True for r in selected)
        rate = successes / count if count else 0.0
        summary[name] = {"episodes": count, "successes": successes, "rate": rate}
        if count < minimum or rate < 0.75:
            errors.append(f"{name}: {successes}/{count} ({rate:.1%}); require >=75% and >={minimum} trials.")
    return summary, errors


def coverage_config(search_config, current_step):
    config = copy.deepcopy(search_config)
    behavior = config["behaviors"]["Explorer"]
    behavior.pop("init_path", None)
    behavior["max_steps"] = max(4_000_000, current_step + 2_000_000)
    parameters = config["environment_parameters"]
    parameters.pop("stair_search_lesson", None)
    parameters["mastery_protocol"] = 1
    parameters.pop("mastery_driver", None)
    parameters["stair_search_shaping"] = 0
    parameters["require_stair_eval_gate"] = 1
    parameters["stair_eval_gate_passed"] = 1
    parameters["floor_lesson"] = {"curriculum": [
        {"name": "BalancedCoverage10Percent", "value": 4, "completion_criteria": {
            "measure": "reward", "behavior": "Explorer", "signal_smoothing": False,
            "min_lesson_length": 100, "threshold": 1000000, "require_reset": True}},
        {"name": "BalancedCoverage20Percent", "value": 5},
    ]}
    return config


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9_-]+", args.run_id):
        parser.error("run-id must be a simple run name, not a path")
    root = Path(__file__).resolve().parents[4]
    report = json.loads(args.report.read_text(encoding="utf-8-sig"))
    summary, errors = validate_report(report)
    if not report.get("model", "").startswith(args.run_id + "_"):
        errors.append("Report model label must start with the selected run-id followed by '_'.")
    run_dir = root / "results" / args.run_id
    export = run_dir / "Explorer.onnx"
    status_file = run_dir / "run_logs" / "training_status.json"
    if not export.is_file() or not status_file.is_file():
        errors.append("Stopped run's final ONNX/training_status not found; no coverage configuration was created.")
    else:
        expected = hashlib.sha256(export.read_bytes()).hexdigest()
        _, hash_errors = validate_report(report, expected)
        errors.extend(e for e in hash_errors if e not in errors)
    print(json.dumps(summary, indent=2))
    if errors:
        print("GATE CLOSED:\n- " + "\n- ".join(errors))
        return 2
    status = json.loads(status_file.read_text(encoding="utf-8-sig"))
    if status.get("floor_lesson", {}).get("lesson_num", 0) != 0:
        print("Coverage has already advanced; keep its existing resume config instead of resetting the lesson.")
        return 2
    step = status["Explorer"]["final_checkpoint"]["steps"]
    # Use the evaluated run's own settings (including opt-in curriculum flags),
    # not another run's hard-coded configuration.
    source = run_dir / "configuration.yaml"
    if not source.is_file():
        print("The evaluated run's configuration.yaml is missing; refusing to guess its training settings.")
        return 2
    config = coverage_config(yaml.safe_load(source.read_text()), step)
    output = source.parent / (args.run_id + "_Coverage_Resume.yaml")
    if output.exists() and yaml.safe_load(output.read_text()) != config:
        print(f"Existing configuration differs; refusing to overwrite {output}")
        return 2
    output.write_text("# Created only after the fixed-seed stair evaluation passed. Use Training/train_phase2.py.\n" +
                      yaml.safe_dump(config, sort_keys=False), encoding="utf-8")
    proof = {"run_id": args.run_id, "checkpoint_step": step,
             "modelSha256": report["modelSha256"], "report": str(args.report.resolve()),
             "summary": summary, "approved_utc": datetime.now(timezone.utc).isoformat()}
    output.with_suffix(".gate.json").write_text(json.dumps(proof, indent=2), encoding="utf-8")
    print(f"GATE PASSED. Resume the same run using: {output.relative_to(root).as_posix()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""Summarize completed collision trials; never treat smoke tests as study data."""
from __future__ import annotations

import argparse
import json
import statistics
from collections import defaultdict
from pathlib import Path


def summarize(directories: list[Path], output: Path) -> dict:
    groups: dict[str, list[dict]] = defaultdict(list)
    manifests: dict[str, dict] = {}
    excluded = []
    runtime_settings: str | None = None
    for directory in directories:
        settings = dict(line.split("=", 1) for line in (directory / "session.txt").read_text(encoding="utf-8-sig").splitlines() if "=" in line)
        if float(settings.get("smokeDurationOverride", "0")) > 0:
            excluded.append({"directory": str(directory), "reason": "smoke test"})
            continue
        settings_path = directory / "runtime-settings.txt"
        if not settings_path.is_file():
            raise ValueError(f"Missing runtime settings: {directory}")
        current_settings = settings_path.read_text(encoding="utf-8-sig")
        if runtime_settings is not None and runtime_settings != current_settings:
            raise ValueError("Movement or observation settings differ across sessions.")
        runtime_settings = current_settings
        for path in sorted(directory.glob("S*_r*.json")):
            trial = json.loads(path.read_text(encoding="utf-8-sig"))
            if trial["status"] != "complete":
                excluded.append({"file": str(path), "reason": trial["status"]})
                continue
            scenario = trial["manifest"]["scenarioId"]
            previous = manifests.setdefault(scenario, trial["manifest"])
            if previous != trial["manifest"]:
                raise ValueError(f"Scenario drift across runs: {scenario}")
            groups[trial["mode"]].append(trial)
    result = {"modes": {}, "excluded": excluded, "scenario_count": len(manifests)}
    protocol_hashes = {trial["protocolSha256"] for trials in groups.values() for trial in trials}
    if len(protocol_hashes) > 1:
        raise ValueError("Protocol differs across sessions. Summarize calibration and final runs separately.")
    for mode, trials in groups.items():
        tp = sum(t["truePositives"] for t in trials)
        fp = sum(t["falsePositives"] for t in trials)
        planted = sum(len(t["manifest"]["defects"]) for t in trials)
        detected_times = [t["firstTrueDetectionSeconds"] for t in trials if t["firstTrueDetectionSeconds"] >= 0]
        types = {}
        for kind, name in ((0, "full_collider"), (1, "partial_hole")):
            total, found = 0, 0
            for trial in trials:
                targets = {d["id"] for d in trial["manifest"]["defects"] if d["kind"] == kind}
                hits = {f.get("matchedDefectId") for f in trial["findings"] if f["truePositive"]}
                total += len(targets)
                found += len(targets & hits)
            types[name] = {"planted": total, "found": found, "recall": found / total if total else None}
        result["modes"][mode] = {
            "trials": len(trials), "planted": planted, "tp": tp, "fp": fp,
            "precision": tp / (tp + fp) if tp + fp else None,
            "recall": tp / planted if planted else None,
            "median_coverage": statistics.median(t["coverage"] for t in trials),
            "median_path_length": statistics.median(t["pathLength"] for t in trials),
            "median_first_true_detection_seconds": statistics.median(detected_times) if detected_times else None,
            "trials_with_true_detection": len(detected_times),
            "clean_trials": sum(not t["manifest"]["defects"] for t in trials),
            "clean_trials_with_false_reports": sum(not t["manifest"]["defects"] and t["falsePositives"] > 0 for t in trials),
            "falls": sum(t["falls"] for t in trials), "by_type": types,
            "simulation_seconds": sum(t["simulationSeconds"] for t in trials),
        }
    output.mkdir(parents=True, exist_ok=True)
    (output / "aggregate.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    lines = ["# Collision pilot results", "", "Descriptive pilot results only. Smoke, interrupted, and failed trials are excluded.", "",
             "| Mode | Trials | Found / planted | False reports | Median coverage | Falls |",
             "|---|---:|---:|---:|---:|---:|"]
    for mode, values in result["modes"].items():
        lines.append(f"| {mode} | {values['trials']} | {values['tp']} / {values['planted']} | {values['fp']} | {values['median_coverage']:.1%} | {values['falls']} |")
    lines += ["", "Coverage counts unique eligible non-stair floor cells physically reached while grounded. Detector sightings do not count as coverage.",
              "", "First-detection medians include only trials with a true detection; non-detections remain missing/censored. Clean scenarios have undefined recall.",
              "", "AI and Random use the same local detector. Human testing must be collected separately; automated manual-workflow checks are not human participant data.",
              "", "These five frozen scenarios are a pilot benchmark, not a claim of population-wide effectiveness or statistical superiority.", ""]
    (output / "RESULTS.md").write_text("\n".join(lines), encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directories", nargs="+", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps(summarize(args.directories, args.output), indent=2))

"""Paired fixed-seed validation and inspection-model selection, not a training gate."""
import hashlib
import json
from pathlib import Path
from statistics import mean

SEEDS = {913701, 921620, 929539, 937458, 945377}


def aggregate(episodes):
    def rate(direction=None):
        rows = [e["result"] for e in episodes if direction is None or e["result"]["direction"] == direction]
        n = len(rows)
        wins = sum(r["success"] for r in rows)
        return {"successes": wins, "episodes": n, "rate": wins / n if n else 0.0}
    rows = [e["result"] for e in episodes]
    return {"success": rate(), "ascent": rate(1), "descent": rate(-1),
            "coverage": mean(r["coverage"] for r in rows),
            "floor0": mean(r["floorZeroCoverage"] for r in rows),
            "floor1": mean(r["floorOneCoverage"] for r in rows),
            "minimum_floor": mean(min(r["floorZeroCoverage"], r["floorOneCoverage"]) for r in rows),
            "stair_entries": sum(r["stairTraversals"] for r in rows),
            "ascent_transitions": sum(r["ascentTransitions"] for r in rows),
            "descent_transitions": sum(r["descentTransitions"] for r in rows),
            "mean_steps": mean(r["steps"] for r in rows),
            "step_limit_episodes": sum(r["stepLimits"] > 0 for r in rows)}


def selection_score(summary):
    # Do not prefer long failed episodes merely because they cover more ground.
    return (min(summary["ascent"]["rate"], summary["descent"]["rate"]),
            summary["success"]["rate"], summary["minimum_floor"], -summary["mean_steps"])


def summarize(directory, labels, run_id):
    directory = Path(directory)
    reports = {label: json.loads((directory / f"{label}.json").read_text(encoding="utf-8-sig")) for label in labels}
    reference = reports[labels[0]]
    results = {}
    for label, report in reports.items():
        if (directory / f"{label}.done").read_text() != "complete" or report.get("error"):
            raise ValueError(f"Incomplete/error evaluation: {label}")
        rows = report["episodes"]
        if len(rows) != 50 or report["requestedEpisodes"] != 50:
            raise ValueError("Exactly 50 episodes per checkpoint required")
        if {e["mapSeed"] for e in rows} != SEEDS:
            raise ValueError("Evaluation map seeds changed")
        actual_hash = hashlib.sha256((directory / "models" / f"{label}.onnx").read_bytes()).hexdigest()
        if report["modelSha256"] != actual_hash:
            raise ValueError(f"Model snapshot hash mismatch: {label}")
        for field in ("protocol", "spawnSeed", "mapSeedBase", "inferenceSeed", "deterministicInference",
                      "mapRegenerationInterval", "fixedDeltaTime", "timeScale"):
            if report[field] != reference[field]:
                raise ValueError(f"Protocol mismatch: {field}")
        for a, b in zip(reference["episodes"], rows):
            for field in ("episode", "mapSeed", "spawn", "target"):
                if a[field] != b[field]:
                    raise ValueError(f"Unpaired {field} in episode {a['episode']}")
            if abs((a["spawnYaw"] - b["spawnYaw"] + 180) % 360 - 180) > 1e-3:
                raise ValueError("Unpaired initial yaw")
            r = b["result"]
            if r["lesson"] != 3 or r["searchShapingActive"] or r["stairPracticeGuided"] or r["stairRehearsal"]:
                raise ValueError("Evaluation includes training assistance or a different lesson")
            if any(a["result"][f] != r[f] for f in ("direction", "spawnStairSteps")):
                raise ValueError("Unpaired direction/distance")
        results[label] = {"aggregate": aggregate(rows),
                          "by_seed": {str(seed): aggregate([e for e in rows if e["mapSeed"] == seed]) for seed in sorted(SEEDS)}}
    candidates = [label for label in labels if label.startswith(run_id + "_")]
    if not candidates:
        raise ValueError("No checkpoints from the trained run")
    selected = max(candidates, key=lambda label: selection_score(results[label]["aggregate"]))
    summary = {"run_id": run_id, "paired_episodes": 50, "results": results,
               "selected_for_inspection": selected, "not_a_phase3_approval": True}
    (directory / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    lines = [f"# Evaluasi headless {run_id}", "",
             "50 episode per checkpoint pada lima seed dengan spawn, yaw, arah dan jarak identik.",
             "Tanpa training, bantuan arah atau shaping pencarian. Hasil ini validasi pemilihan model, bukan uji akhir tesis yang independen.", "",
             "| Model | Berhasil | Naik | Turun | Coverage total | Bawah | Atas | Masuk tangga | Transfer naik/turun | Rata-rata step |",
             "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"]
    for label in labels:
        s = results[label]["aggregate"]
        def fmt(key):
            x = s[key]
            return f"{x['successes']}/{x['episodes']} ({x['rate']:.1%})"
        lines.append(f"| {label} | {fmt('success')} | {fmt('ascent')} | {fmt('descent')} | "
                     f"{s['coverage']:.2%} | {s['floor0']:.2%} | {s['floor1']:.2%} | "
                     f"{s['stair_entries']} | {s['ascent_transitions']}/{s['descent_transitions']} | {s['mean_steps']:.1f} |")
    lines += ["", "## Konsistensi per seed", "", "| Seed | " + " | ".join(labels) + " |",
              "| --- | " + " | ".join("---:" for _ in labels) + " |"]
    for seed in sorted(SEEDS):
        cells = [results[label]["by_seed"][str(seed)]["success"] for label in labels]
        lines.append(f"| {seed} | " + " | ".join(f"{x['successes']}/{x['episodes']}" for x in cells) + " |")
    lines += ["", f"Model terpilih untuk ditonton: **{selected}**, hanya terbaik di antara checkpoint baru yang diuji.",
              "Pemilihan mengutamakan keberhasilan arah terlemah, lalu keberhasilan keseluruhan; bukan mean reward training.",
              "Masuk trigger tangga tidak sama dengan transfer lantai selesai. Episode berhasil berakhir setelah transfer, sehingga coverage bukan eksplorasi panjang.",
              "Lima seed dan satu seed inference belum membuktikan generalisasi atau signifikansi statistik. Tidak otomatis lanjut coverage/Phase 3.", ""]
    (directory / "HASIL_EVALUASI.md").write_text("\n".join(lines), encoding="utf-8")
    return summary

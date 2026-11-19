"""Read-only episode analysis; writes report/summary only inside evaluation output."""
import argparse
import json
import math
from pathlib import Path
from statistics import mean
from summarize_coverage_validation import aggregate

MODEL_SHA256 = "3ce43d2242dbb63a6dea7db5c98d6c58248084a69736c70c47f16561857eba38"
CONDITIONS = {"steps7500": 7500, "steps12000": 12000}


def validate_pair(reports):
    """Reject changed models, incomplete batches, unpaired spawns and invalid wins."""
    for label, limit in CONDITIONS.items():
        r = reports[label]
        if (r.get("error") or r["protocol"] != "phase3-paired-budget-level8-v1"
            or r["modelSha256"] != MODEL_SHA256 or r["model"] != "Phase3_4000073"
            or not r["deterministicInference"] or r["requestedEpisodes"] != 20
            or len(r["episodes"]) != 20 or r["maxStep"] != limit
            or r["mapRegenerationInterval"] != 4 or r["mapSeedBase"] != 1427003
            or r["spawnSeed"] != 27421 or r["inferenceSeed"] != 58231
            or abs(r["fixedDeltaTime"] - 0.02) > 1e-6 or r["timeScale"] != 40
            or abs(r["coverageTarget"] - 0.1) > 1e-6 or abs(r["maximumFloorDifference"] - 0.1) > 1e-6):
            raise ValueError(f"Incomplete or changed protocol: {label}")
        seeds = {e["episode"]["mapSeed"] for e in r["episodes"]}
        if len(seeds) != 5 or any(sum(x["episode"]["mapSeed"] == s for x in r["episodes"]) != 4 for s in seeds):
            raise ValueError("Expected five map batches, four episodes each.")
        for n, entry in enumerate(r["episodes"], 1):
            e = entry["episode"]; result = e["result"]
            if (e["episode"] != n or result["lesson"] != 4 or result["searchShapingActive"]
                or result["stairPracticeGuided"] or result["stairRehearsal"]
                or not 0 < result["steps"] <= limit):
                raise ValueError("Invalid episode, steps, lesson or guidance.")
            a, b = result["floorZeroCoverage"], result["floorOneCoverage"]
            if not all(math.isfinite(v) and 0 <= v <= 1 for v in (a, b)):
                raise ValueError("Invalid coverage.")
            if result["success"] and (min(a, b) < 0.1 - 1e-5 or abs(a-b) > 0.1 + 1e-5
                                      or result["ascentTransitions"] + result["descentTransitions"] < 1):
                raise ValueError("Success violates balanced 10% coverage + transfer.")
            if entry["has7500Snapshot"]:
                if not 7500 <= entry["snapshotStep"] <= result["steps"]:
                    raise ValueError("Invalid snapshot step.")
                if not all(math.isfinite(entry[k]) and 0 <= entry[k] <= 1 for k in ("snapshotFloor0", "snapshotFloor1")):
                    raise ValueError("Invalid snapshot coverage.")
                if (entry["snapshotFloor0"] > a + 1e-5 or entry["snapshotFloor1"] > b + 1e-5):
                    raise ValueError("Coverage decreased within an episode.")
            elif result["steps"] >= 7500:
                raise ValueError("Missing 7500 snapshot.")
    for short, long in zip(reports["steps7500"]["episodes"], reports["steps12000"]["episodes"]):
        a, b = short["episode"], long["episode"]
        if a["mapSeed"] != b["mapSeed"] or a["result"]["startFloor"] != b["result"]["startFloor"]:
            raise ValueError("Unpaired map seed/start floor.")
        for key in ("spawn", "target"):
            if any(abs(a[key][axis] - b[key][axis]) > 1e-5 for axis in ("x", "y", "z")):
                raise ValueError("Unpaired spawn/target.")
        if abs((a["spawnYaw"]-b["spawnYaw"]+180) % 360 - 180) > 1e-4:
            raise ValueError("Unpaired yaw.")


def compare(reports):
    validate_pair(reports)
    results = {}
    for label, r in reports.items():
        episodes = [x["episode"] for x in r["episodes"]]
        results[label] = {"aggregate": aggregate(episodes),
            "by_seed": {str(s): aggregate([e for e in episodes if e["mapSeed"] == s])
                        for s in sorted({e["mapSeed"] for e in episodes})}}
    pairs = [(a["episode"]["result"], b["episode"]["result"])
             for a, b in zip(reports["steps7500"]["episodes"], reports["steps12000"]["episodes"])]
    new_wins = sum(not a["success"] and b["success"] for a, b in pairs)
    lost_wins = sum(a["success"] and not b["success"] for a, b in pairs)
    delta = [min(b["floorZeroCoverage"], b["floorOneCoverage"])
             - min(a["floorZeroCoverage"], a["floorOneCoverage"]) for a, b in pairs]
    snapshots = [x for x in reports["steps12000"]["episodes"] if x["has7500Snapshot"]]
    after_gain = [min(x["episode"]["result"]["floorZeroCoverage"], x["episode"]["result"]["floorOneCoverage"])
                  - min(x["snapshotFloor0"], x["snapshotFloor1"]) for x in snapshots]
    return {"protocol": "phase3-paired-budget-level8-v1", "model_sha256": MODEL_SHA256,
        "paired_seed_spawn_target_yaw_verified": True, "training": False,
        "independent_final_thesis_test": False, "results": results,
        "paired": {"episodes": 20, "new_successes": new_wins, "lost_successes": lost_wins,
            "mean_minimum_floor_delta": mean(delta), "minimum_floor_improved": sum(v > 1e-5 for v in delta),
            "minimum_floor_worsened": sum(v < -1e-5 for v in delta)},
        "long_run_after_7500": {"episodes": len(snapshots), "mean_minimum_floor_gain": mean(after_gain) if after_gain else 0,
            "improved": sum(v > 1e-5 for v in after_gain)},
        "diagnostics": {k:r.get("diagnostics", []) for k,r in reports.items()},
        "caveat": "StepCount/MaxStep is an observation: changing MaxStep can change the path from the first action. Not a same-trajectory continuation."}


def summarize(directory):
    directory = Path(directory)
    reports = {}
    for label in CONDITIONS:
        if (directory / (label + ".done")).read_text() != "complete":
            raise ValueError("Incomplete condition.")
        reports[label] = json.loads((directory / (label + ".json")).read_text(encoding="utf-8-sig"))
    s = compare(reports)
    (directory / "summary.json").write_text(json.dumps(s, indent=2), encoding="utf-8")
    a = s["results"]["steps7500"]["aggregate"]; b = s["results"]["steps12000"]["aggregate"]
    lines = ["# Uji Max Step Phase 3 — 27 Oktober 2026", "",
        "Model Phase3 step 4.000.073; inference deterministik, Burst CPU, speed 6, time scale 40x, physics dt 0,02.",
        "Dua kondisi: 7.500 vs 12.000 physics step per episode. Lima seed, empat episode per seed: 20 pasangan/40 episode.",
        "Seed map, spawn, target dan yaw setiap pasangan diverifikasi sama. Scene 24x24x2 dan seluruh parameter lain sama.",
        "Target lesson aktif: minimal 10% SETIAP lantai, selisih maksimal 10 poin persentase, harus berpindah lantai.",
        "Tidak ada training, perubahan reward/collider, action assistance, stair-search shaping, atau perubahan scene utama.", "",
        "| Metrik | Max Step 7.500 | Max Step 12.000 |", "|---|---:|---:|"]
    for title, key, fmt in [("Episode sukses", "successes", "count"),
        ("Mencapai lantai lain", "other_floor_reached", "count"),
        ("Coverage bawah", "floor0", "pct"), ("Coverage atas", "floor1", "pct"),
        ("Minimum coverage per episode", "minimum_floor", "pct"),
        ("Selisih coverage per episode", "floor_difference", "pp"),
        ("Transfer naik selesai (total)", "ascent_transitions", "int"),
        ("Transfer turun selesai (total)", "descent_transitions", "int"),
        ("Trigger tangga unik per episode (total)", "stair_entries", "int"),
        ("Step rata-rata", "mean_steps", "float"), ("Revisit rata-rata", "mean_revisits", "float"),
        ("Short loop rata-rata", "mean_short_loops", "float"),
        ("Episode mencapai batas step", "step_limit_episodes", "count"),
        ("Episode timeout tanpa area baru", "timeout_episodes", "count"),
        ("Episode gagal displacement", "stationary_episodes", "count")]:
        def render(x):
            v = x[key]
            return (f"{v}/20 ({v/20:.0%})" if fmt == "count" else f"{v:.2%}" if fmt == "pct"
                    else f"{v*100:.2f} pp" if fmt == "pp" else f"{v:.2f}" if fmt == "float" else str(v))
        lines.append(f"| {title} | {render(a)} | {render(b)} |")
    p = s["paired"]; x = s["long_run_after_7500"]
    lines += ["", "## Pasangan dan waktu tambahan", "",
        f"12.000 step menghasilkan {p['new_successes']} sukses baru dan kehilangan {p['lost_successes']} sukses dibanding pasangan 7.500.",
        f"Minimum coverage lebih baik pada {p['minimum_floor_improved']}/20 pasangan, lebih buruk pada {p['minimum_floor_worsened']}/20; delta rata-rata {p['mean_minimum_floor_delta']*100:.2f} pp.",
        f"Dalam kondisi 12.000 sendiri, {x['episodes']} episode mencapai step 7.500. Dari sana minimum coverage bertambah pada {x['improved']} episode, rata-rata bertambah {x['mean_minimum_floor_gain']*100:.2f} pp pada kelompok itu.",
        "", "## Per seed", "", "| Seed | Sukses 7.500 / 12.000 | Minimum coverage 7.500 / 12.000 |", "|---|---:|---:|"]
    for seed, v in s["results"]["steps7500"]["by_seed"].items():
        w = s["results"]["steps12000"]["by_seed"][seed]
        lines.append(f"| {seed} | {v['successes']}/4 / {w['successes']}/4 | {v['minimum_floor']:.1%} / {w['minimum_floor']:.1%} |")
    lines += ["", "## Interpretasi dan langkah berikutnya", "",
        "Pada pengujian ini 12.000 step meningkatkan keberhasilan dan minimum coverage, tetapi belum menunjukkan penguasaan yang konsisten.",
        f"Keberhasilan naik dari {a['successes']}/20 ke {b['successes']}/20. Selisih coverage rata-rata antar lantai justru {a['floor_difference']*100:.2f} → {b['floor_difference']*100:.2f} pp; tidak semua aspek membaik.",
        f"Untuk spawn bawah: mencapai atas {a['ascent']['reached']}/{a['ascent']['episodes']} → {b['ascent']['reached']}/{b['ascent']['episodes']}. Untuk spawn atas: mencapai bawah {a['descent']['reached']}/{a['descent']['episodes']} → {b['descent']['reached']}/{b['descent']['episodes']}.",
        "Saran: gunakan 12.000 sebagai kandidat Max Step fine-tuning berikutnya dari checkpoint 4.000.073, bukan mengulang Phase 1. Budget tambahan harus diputuskan pengguna.",
        "Tidak boleh menganggap model sudah menguasai target 20% atau siap uji tesis hanya dari uji target 10% ini. Scene produksi dan YAML belum diubah oleh evaluasi."]
    lines += ["", "## Batas interpretasi", "",
        "- Slot observasi StepCount/MaxStep berubah saat budget berubah. Ini bukan meneruskan lintasan yang identik; perilaku bisa berbeda dari awal.",
        "- Episode sukses berhenti pada target 10% seimbang, bukan mengukur kemampuan eksplorasi maksimal sampai 100%.",
        "- Rata-rata dua lantai tidak sama dengan keseimbangan dalam episode yang sama. Gunakan minimum coverage per episode.",
        "- Revisit dapat diperlukan untuk kembali ke tangga. Trigger tangga dideduplikasi per trigger per episode, bukan semua kejadian masuk.",
        "- Lima seed dan 20 pasangan merupakan diagnosis pengembangan, bukan bukti generalisasi atau uji akhir tesis independen.",
        "- Hasil deterministik pada seed baru tidak identik dengan statistik training yang memakai sampling aksi.",
        "- Pesan editor SearchDatabase, jika ada, tersimpan terpisah; semua error agent/inference menggagalkan pengujian.",
        "- Pengujian ini tidak otomatis mengubah Max Step scene produksi, budget training, atau checkpoint."]
    (directory / "HASIL_UJI.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return s


if __name__ == "__main__":
    parser = argparse.ArgumentParser(); parser.add_argument("directory")
    print(json.dumps(summarize(parser.parse_args().directory), indent=2))

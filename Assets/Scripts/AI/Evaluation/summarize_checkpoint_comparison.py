"""Paired checkpoint comparison; never writes models, scenes or trainer state."""
import argparse
import json
import math
from pathlib import Path
from statistics import mean
from summarize_coverage_validation import aggregate

MODELS = {
    "checkpoint4499994": ("Phase3_4499994", "c9263e534c7ab3cb0ae2aa51995ad2a826203276140cf78fc48f4c0bec68121a"),
    "checkpoint6000091": ("Phase3_6000091", "2572bc76efc781d2de436c755a17a10c9ab71400c74b8253e383cfd7317e30a7"),
}


def validate(reports):
    for label, (model, digest) in MODELS.items():
        r = reports[label]
        if (r.get("error") or r["protocol"] != "phase3-paired-checkpoints-level8-v1"
            or r["model"] != model or r["modelSha256"] != digest or not r["deterministicInference"]
            or r["requestedEpisodes"] != 50 or len(r["episodes"]) != 50 or r["maxStep"] != 12000
            or r["mapRegenerationInterval"] != 10 or r["mapSeedBase"] != 1427003
            or r["spawnSeed"] != 27421 or r["inferenceSeed"] != 58231 or r["timeScale"] != 40
            or abs(r["fixedDeltaTime"] - .02) > 1e-6 or abs(r["coverageTarget"] - .1) > 1e-6
            or abs(r["maximumFloorDifference"] - .1) > 1e-6):
            raise ValueError(f"Incomplete/changed model or protocol: {label}")
        seeds = {x["episode"]["mapSeed"] for x in r["episodes"]}
        if len(seeds) != 5 or any(sum(x["episode"]["mapSeed"] == seed for x in r["episodes"]) != 10 for seed in seeds):
            raise ValueError("Expected five map batches of ten episodes.")
        for i, entry in enumerate(r["episodes"], 1):
            e = entry["episode"]; s = e["result"]
            if (e["episode"] != i or not 0 < s["steps"] <= 12000 or s["lesson"] != 4
                or s["searchShapingActive"] or s["stairPracticeGuided"] or s["stairRehearsal"]):
                raise ValueError("Invalid episode/steps or unexpected guidance.")
            lo, hi = s["floorZeroCoverage"], s["floorOneCoverage"]
            if not all(math.isfinite(v) and 0 <= v <= 1 for v in (lo, hi)):
                raise ValueError("Invalid coverage values.")
            if s["success"] and (min(lo, hi) < .1-1e-5 or abs(lo-hi) > .1+1e-5
                                  or s["ascentTransitions"]+s["descentTransitions"] < 1):
                raise ValueError("Success violates balanced coverage and floor transfer.")
            if entry["has7500Snapshot"]:
                if not 7500 <= entry["snapshotStep"] <= s["steps"]:
                    raise ValueError("Invalid snapshot time.")
                if not all(math.isfinite(entry[k]) and 0 <= entry[k] <= 1 for k in ("snapshotFloor0", "snapshotFloor1")):
                    raise ValueError("Invalid snapshot coverage.")
                if entry["snapshotFloor0"] > lo+1e-5 or entry["snapshotFloor1"] > hi+1e-5:
                    raise ValueError("Coverage decreased within an episode.")
            elif s["steps"] >= 7500:
                raise ValueError("Missing 7500 snapshot.")
    for a, b in zip(reports["checkpoint4499994"]["episodes"], reports["checkpoint6000091"]["episodes"]):
        a, b = a["episode"], b["episode"]
        if a["mapSeed"] != b["mapSeed"] or a["result"]["startFloor"] != b["result"]["startFloor"]:
            raise ValueError("Unpaired map/start floor.")
        for field in ("spawn", "target"):
            if any(abs(a[field][axis]-b[field][axis]) > 1e-5 for axis in ("x", "y", "z")):
                raise ValueError("Unpaired spawn/target.")
        if abs((a["spawnYaw"]-b["spawnYaw"]+180) % 360-180) > 1e-4:
            raise ValueError("Unpaired yaw.")


def describe(episodes):
    result = aggregate(episodes)
    steps = sum(e["result"]["steps"] for e in episodes)
    result["revisits_per_1000_steps"] = sum(e["result"]["revisits"] for e in episodes)*1000/steps
    result["collisions_per_1000_steps"] = sum(e["result"]["collisions"] for e in episodes)*1000/steps
    return result


def compare(reports):
    validate(reports)
    results = {}
    for label, report in reports.items():
        episodes = [x["episode"] for x in report["episodes"]]
        results[label] = {"aggregate": describe(episodes),
            "by_seed": {str(s): describe([e for e in episodes if e["mapSeed"] == s])
                        for s in sorted({e["mapSeed"] for e in episodes})}}
    pairs = [(a["episode"]["result"], b["episode"]["result"])
             for a,b in zip(reports["checkpoint4499994"]["episodes"], reports["checkpoint6000091"]["episodes"])]
    delta = [min(b["floorZeroCoverage"], b["floorOneCoverage"])-min(a["floorZeroCoverage"],a["floorOneCoverage"]) for a,b in pairs]
    return {"protocol": "phase3-paired-checkpoints-level8-v1", "training": False,
        "not_independent_final_thesis_test": True, "paired_seed_spawn_target_yaw_verified": True,
        "results": results, "paired": {"episodes": 50,
            "only4499994_success": sum(a["success"] and not b["success"] for a,b in pairs),
            "only6000091_success": sum(not a["success"] and b["success"] for a,b in pairs),
            "both_success": sum(a["success"] and b["success"] for a,b in pairs),
            "neither_success": sum(not a["success"] and not b["success"] for a,b in pairs),
            "minimum_floor_better6000091": sum(v > 1e-5 for v in delta),
            "minimum_floor_better4499994": sum(v < -1e-5 for v in delta),
            "mean_minimum_floor_delta6000091_minus4499994": mean(delta)},
        "diagnostics": {k:r.get("diagnostics",[]) for k,r in reports.items()}}


def summarize(directory):
    directory = Path(directory)
    reports = {}
    for label in MODELS:
        if (directory/(label+".done")).read_text() != "complete":
            raise ValueError("Incomplete evaluation.")
        reports[label] = json.loads((directory/(label+".json")).read_text(encoding="utf-8-sig"))
    s = compare(reports)
    (directory/"summary.json").write_text(json.dumps(s,indent=2),encoding="utf-8")
    a = s["results"]["checkpoint4499994"]["aggregate"]; b = s["results"]["checkpoint6000091"]["aggregate"]
    lines = ["# Perbandingan checkpoint Phase 3 — 27 Oktober 2026", "",
        "Checkpoint 4.499.994 versus 6.000.091. Inference deterministik, Burst CPU, tanpa training.",
        "Max Step 12.000 KEDUANYA; speed 6; time scale 40x; physics dt 0,02; input tetap 177.",
        "50 pasangan episode pada lima map (10 episode/map). Seed, spawn, target, yaw dan lantai spawn diverifikasi sama.",
        "Target level 8: coverage minimal 10% setiap lantai, selisih maksimal 10 poin persentase dalam episode yang sama, harus berpindah lantai.",
        "Tidak ada rehearsal, guided transfer, action assistance, atau stair-search shaping. Reward/timeout/collider scene utama dipertahankan.", "",
        "| Metrik | 4.499.994 | 6.000.091 |", "|---|---:|---:|"]
    fields = [("Episode sukses", "successes", "count"), ("Mencapai lantai lain", "other_floor_reached", "count"),
        ("Coverage bawah rata-rata", "floor0", "pct"), ("Coverage atas rata-rata", "floor1", "pct"),
        ("Minimum coverage per episode", "minimum_floor", "pct"), ("Selisih coverage per episode", "floor_difference", "pp"),
        ("Transfer naik selesai (total)", "ascent_transitions", "int"), ("Transfer turun selesai (total)", "descent_transitions", "int"),
        ("Trigger tangga unik per episode (total)", "stair_entries", "int"),
        ("Step rata-rata", "mean_steps", "float"), ("Revisit rata-rata", "mean_revisits", "float"),
        ("Revisit per 1.000 step", "revisits_per_1000_steps", "float"), ("Short loop rata-rata", "mean_short_loops", "float"),
        ("Collision per 1.000 step", "collisions_per_1000_steps", "float"),
        ("Episode mencapai batas step", "step_limit_episodes", "count"),
        ("Episode timeout tanpa area baru", "timeout_episodes", "count"),
        ("Episode gagal displacement", "stationary_episodes", "count")]
    for title, key, fmt in fields:
        def value(x):
            v = x[key]
            return (f"{v}/50 ({v/50:.0%})" if fmt == "count" else f"{v:.2%}" if fmt == "pct"
                    else f"{v*100:.2f} pp" if fmt == "pp" else f"{v:.2f}" if fmt == "float" else str(v))
        lines.append(f"| {title} | {value(a)} | {value(b)} |")
    lines += ["", "## Arah transfer menurut lantai spawn", "",
        f"- Spawn bawah → mencapai atas: {a['ascent']['reached']}/{a['ascent']['episodes']} vs {b['ascent']['reached']}/{b['ascent']['episodes']}.",
        f"- Spawn atas → mencapai bawah: {a['descent']['reached']}/{a['descent']['episodes']} vs {b['descent']['reached']}/{b['descent']['episodes']}.",
        "", "## Konsistensi per seed", "", "| Seed | Sukses 4.499.994 / 6.000.091 | Minimum coverage 4.499.994 / 6.000.091 |", "|---|---:|---:|"]
    for seed,x in s["results"]["checkpoint4499994"]["by_seed"].items():
        y = s["results"]["checkpoint6000091"]["by_seed"][seed]
        lines.append(f"| {seed} | {x['successes']}/10 / {y['successes']}/10 | {x['minimum_floor']:.1%} / {y['minimum_floor']:.1%} |")
    p = s["paired"]
    lines += ["", "## Hasil pasangan", "",
        f"Hanya 4.499.994 sukses: {p['only4499994_success']}; hanya 6.000.091 sukses: {p['only6000091_success']}; keduanya sukses: {p['both_success']}; keduanya gagal: {p['neither_success']}.",
        f"Minimum coverage lebih tinggi pada model 4.499.994 dalam {p['minimum_floor_better4499994']} pasangan, pada model 6.000.091 dalam {p['minimum_floor_better6000091']} pasangan.",
        "", "## Interpretasi", "",
        f"Model 6.000.091 menghasilkan {b['successes']-a['successes']} sukses lebih banyak dari 50 episode ({(b['successes']-a['successes'])/50*100:.0f} poin persentase), tetapi minimum coverage hampir sama ({a['minimum_floor']:.2%} vs {b['minimum_floor']:.2%}).",
        f"Selisih coverage antar lantai justru {a['floor_difference']*100:.2f} vs {b['floor_difference']*100:.2f} pp. Jadi model akhir tidak unggul pada semua metrik.",
        "Hasil setara ini tidak mendukung kesimpulan bahwa checkpoint akhir pasti mengalami penurunan besar seperti statistik bagian akhir training. Map training berbeda-beda; performa juga bergantung seed.",
        "Tidak ada bukti kuat dari uji ini untuk rollback otomatis. Model 6 juta dapat dipertahankan sebagai kandidat utama untuk pengamatan lanjutan, sedangkan 4,5 juta disimpan sebagai pembanding. Ini bukan klaim keunggulan statistik atau penguasaan coverage.",
        "Tidak ada training baru, perluasan budget, pemindahan checkpoint aktif, atau pemasangan model ke scene yang dilakukan otomatis.",
        "", "## Batas interpretasi", "",
        "- Ini validasi pemilihan checkpoint pada seed pengembangan, bukan uji akhir tesis independen. Lima seed juga dipakai pada diagnosis Max Step sebelumnya.",
        "- Tidak menyimpulkan peningkatan/penurunan universal atau signifikansi statistik dari satu run training dan lima map.",
        "- Target yang diuji 10%, bukan penguasaan 20%, 40% atau eksplorasi penuh. Episode sukses berhenti pada ambang tersebut.",
        "- Gunakan minimum coverage PER EPISODE, bukan kesamaan rata-rata coverage antar episode.",
        "- Trigger tangga dideduplikasi per trigger per episode; transfer naik/turun adalah pergantian lantai terkonfirmasi, bukan sekadar menyentuh tangga.",
        "- Revisit dapat diperlukan untuk kembali ke tangga. Short-loop 0 bukan bukti tidak ada rute panjang yang diulang.",
        "- Statistik training memakai map/sampling aksi berbeda, sehingga tidak dapat dibandingkan langsung sebagai pengujian checkpoint yang setara.",
        "- Semua error agent/inference menggagalkan pengujian. Diagnostik editor SearchDatabase yang terpisah tersimpan di JSON.",
        "- Scene, konfigurasi trainer, checkpoint aktif dan seluruh model sumber tidak diganti oleh pengujian ini."]
    (directory/"HASIL_UJI.md").write_text("\n".join(lines)+"\n",encoding="utf-8")
    return s


if __name__ == "__main__":
    p = argparse.ArgumentParser();p.add_argument("directory")
    print(json.dumps(summarize(p.parse_args().directory),indent=2))

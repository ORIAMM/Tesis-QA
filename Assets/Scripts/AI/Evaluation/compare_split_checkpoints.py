"""Strict paired, inference-only comparison. Writes reports, never model/trainer data."""
import argparse
import csv
import itertools
import json
import math
from pathlib import Path
from statistics import mean

from summarize_floor_timing import validate as validate_timing, describe, SEEDS

PROTOCOL = "phase3s-paired-three-models-level8-v1"
MODELS = {
    "baseline2000063": ("Phase3R_2000063", "a6a6410d44a5bffa810553e1f0baa3b9fe4b699a2c8d73c32eb00c6c32d7a9a2"),
    "split499998": ("Phase3S_499998", "5ae504859164ec75fe66226db432808735ab562231eb0b87488ffbbac06e554c"),
    "split1000061": ("Phase3S_1000061", "3265d28da02036e5997f69ab64fa64daa05c0f776429a03752f1baeb661a87f9"),
}
NAMES = {"baseline2000063": "Phase3R 2M", "split499998": "Phase3S 500k", "split1000061": "Phase3S 1M"}


def validate(reports):
    if set(reports) != set(MODELS): raise ValueError("Expected exactly three model conditions.")
    for label, (model, digest) in MODELS.items():
        r = reports[label]
        validate_timing(r, model=model, digest=digest, protocol=PROTOCOL, revision=2)
        if r["condition"] != label or r["mapSeedBase"] != SEEDS[0]:
            raise ValueError("Changed condition or map seed base.")
        for entry in r["episodes"]:
            e = entry["episode"]; s = e["result"]
            if not all(math.isfinite(e[field][axis]) for field in ("spawn", "target") for axis in ("x", "y", "z")) or not math.isfinite(e["spawnYaw"]):
                raise ValueError("Non-finite spawn/target/yaw.")
            if s["success"] and s["ascentTransitions"] + s["descentTransitions"] < 1:
                raise ValueError("Success without a confirmed floor transition.")
            if entry["has7500Snapshot"]:
                if not 7500 <= entry["snapshotStep"] <= s["steps"]:
                    raise ValueError("Invalid 7500 snapshot step.")
                if not (0 <= entry["snapshotFloor0"] <= s["floorZeroCoverage"]+1e-6
                        and 0 <= entry["snapshotFloor1"] <= s["floorOneCoverage"]+1e-6):
                    raise ValueError("Invalid or decreasing snapshot coverage.")
            elif s["steps"] >= 7500: raise ValueError("Missing 7500 snapshot.")
    baseline = reports["baseline2000063"]["episodes"]
    for label in ("split499998", "split1000061"):
        for a, b in zip(baseline, reports[label]["episodes"]):
            a, b = a["episode"], b["episode"]
            if a["mapSeed"] != b["mapSeed"] or a["result"]["startFloor"] != b["result"]["startFloor"]:
                raise ValueError("Unpaired seed or start floor.")
            for field in ("spawn", "target"):
                if any(abs(a[field][axis]-b[field][axis]) > 1e-5 for axis in ("x", "y", "z")):
                    raise ValueError("Unpaired spawn or target.")
            if abs((a["spawnYaw"]-b["spawnYaw"]+180) % 360-180) > 1e-4:
                raise ValueError("Unpaired spawn yaw.")


def paired(a, b):
    pairs = [(x["episode"]["result"], y["episode"]["result"]) for x, y in zip(a, b)]
    delta = [min(y["floorZeroCoverage"], y["floorOneCoverage"])
             - min(x["floorZeroCoverage"], x["floorOneCoverage"]) for x, y in pairs]
    common = [(x, y) for x, y in pairs if x["floorTiming"]["firstOtherFloorStep"] >= 0
              and y["floorTiming"]["firstOtherFloorStep"] >= 0]
    return {"episodes": len(pairs),
        "only_a_success": sum(x["success"] and not y["success"] for x, y in pairs),
        "only_b_success": sum(not x["success"] and y["success"] for x, y in pairs),
        "both_success": sum(x["success"] and y["success"] for x, y in pairs),
        "neither_success": sum(not x["success"] and not y["success"] for x, y in pairs),
        "minimum_floor_better_b": sum(d > 1e-5 for d in delta),
        "minimum_floor_better_a": sum(d < -1e-5 for d in delta),
        "mean_minimum_floor_delta_b_minus_a": mean(delta),
        "both_physical_arrival_episodes": len(common),
        "mean_first_arrival_step_delta_b_minus_a_common_arrivals": mean(
            y["floorTiming"]["firstOtherFloorStep"]-x["floorTiming"]["firstOtherFloorStep"] for x, y in common) if common else None}


def details(entries):
    d = describe(entries)
    d["physical_by_start_floor"] = {}
    for floor in (0, 1):
        group = [x["episode"]["result"] for x in entries if x["episode"]["result"]["startFloor"] == floor]
        d["physical_by_start_floor"][str(floor)] = {"episodes": len(group),
            "reached": sum(x["floorTiming"]["firstOtherFloorStep"] >= 0 for x in group)}
    return d


def compare(reports):
    validate(reports)
    return {"protocol": PROTOCOL, "training": False, "reward_revision_all_models": 2,
        "paired_seed_spawn_target_yaw_verified": True, "not_independent_thesis_test": True,
        "results": {label: {"model": MODELS[label][0], "sha256": MODELS[label][1],
            "overall": details(r["episodes"]),
            "by_seed": {str(seed): details([x for x in r["episodes"] if x["episode"]["mapSeed"] == seed]) for seed in SEEDS}}
            for label, r in reports.items()},
        "paired": {a+"__"+b: paired(reports[a]["episodes"], reports[b]["episodes"])
                   for a, b in itertools.combinations(MODELS, 2)},
        "editor_diagnostics": {label: r.get("diagnostics", []) for label, r in reports.items()}}


def summarize(directory):
    directory = Path(directory)
    reports = {}
    for label in MODELS:
        if (directory/(label+".done")).read_text() != "complete": raise ValueError("Incomplete evaluation: " + label)
        reports[label] = json.loads((directory/(label+".json")).read_text(encoding="utf-8-sig"))
    s = compare(reports)
    (directory/"summary.json").write_text(json.dumps(s, indent=2), encoding="utf-8")
    rows = []
    for label, report in reports.items():
        for entry in report["episodes"]:
            e = entry["episode"]; r = e["result"]
            rows.append(dict(condition=label, episode=e["episode"], seed=e["mapSeed"],
                start_floor_index=r["startFloor"], success=r["success"], steps=r["steps"],
                coverage_floor0=r["floorZeroCoverage"], coverage_floor1=r["floorOneCoverage"],
                ascent_transitions=r["ascentTransitions"], descent_transitions=r["descentTransitions"],
                stair_entries_unique=r["stairTraversals"], revisits=r["revisits"], **r["floorTiming"]))
    with (directory/"PER_EPISODE.csv").open("w", encoding="utf-8-sig", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=list(rows[0])); writer.writeheader(); writer.writerows(rows)
    labels = list(MODELS)
    lines = ["# Perbandingan berpasangan Phase3R / Phase3S", "",
        "28 Oktober 2026. Inference saja, 50 episode/model (total 150), lima seed pengembangan, 10 episode per seed.",
        "SEMUA model memakai reward revision 2, target 10% tiap lantai, selisih <=10 pp, MaxStep 12.000, speed 6, time scale 40x, dt 0,02 detik.",
        "Spawn seed 27421; inference seed 58231; seed map 1427003, 1434922, 1442841, 1450760, 1458679. Seed, spawn, target, start floor dan yaw diverifikasi identik per pasangan.",
        "Model deterministik, tanpa trainer, guidance, rehearsal, atau planner. Semua scene pengujian tidak disimpan dan berada di .ai-eval.", "",
        "Baseline diuji ulang pada revision 2, BUKAN mengambil ulang angka revision 1 lama. Ini membandingkan checkpoint, bukan ablation kausal murni efek reward: Phase3S juga mendapat tambahan training.", "",
        "## Hasil utama", "", "| Ukuran | " + " | ".join(NAMES[x] for x in labels) + " |", "|---|---:|---:|---:|"]
    def total(label): return s["results"][label]["overall"]
    fields = [("Sukses coverage seimbang", lambda d: f"{d['aggregate']['successes']}/50"),
        ("Mencapai lantai lain secara fisik", lambda d: f"{d['physical_other_floor_reached']}/50"),
        ("Naik dari spawn bawah (fisik)", lambda d: f"{d['physical_by_start_floor']['0']['reached']}/{d['physical_by_start_floor']['0']['episodes']}"),
        ("Turun dari spawn atas (fisik)", lambda d: f"{d['physical_by_start_floor']['1']['reached']}/{d['physical_by_start_floor']['1']['episodes']}"),
        ("Coverage lantai 1", lambda d: f"{d['aggregate']['floor0']:.2%}"),
        ("Coverage lantai 2", lambda d: f"{d['aggregate']['floor1']:.2%}"),
        ("Minimum coverage per episode", lambda d: f"{d['aggregate']['minimum_floor']:.2%}"),
        ("Selisih coverage per episode", lambda d: f"{d['aggregate']['floor_difference']*100:.2f} pp"),
        ("Mean step episode", lambda d: f"{d['aggregate']['mean_steps']:.0f}"),
        ("Revisit per episode", lambda d: f"{d['aggregate']['mean_revisits']:.2f}"),
        ("Total transfer naik / turun (counter terkonfirmasi)", lambda d: f"{d['aggregate']['ascent_transitions']} / {d['aggregate']['descent_transitions']}"),
        ("Trigger tangga unik, total", lambda d: str(d['aggregate']['stair_entries'])),
        ("Episode mencapai MaxStep", lambda d: str(d['aggregate']['step_limit_episodes'])),
        ("Episode timeout area baru", lambda d: str(d['aggregate']['timeout_episodes']))]
    for name, fmt in fields: lines.append("| " + name + " | " + " | ".join(fmt(total(x)) for x in labels) + " |")
    lines += ["", "## Waktu dan coverage setelah perpindahan pertama", "",
        "Detik adalah waktu simulasi, bukan wall-clock pada 40x. Kedatangan fisik = grounded di permukaan <=10 derajat dan kaki <=0,35 unit dari bidang lantai lain. Ramp/udara/ground tidak jelas dipisahkan.",
        "Waktu lantai dihitung untuk SEMUA 50 episode. Step kedatangan dan gain coverage hanya episode yang mencapai lantai lain; jumlahnya berbeda antarmodel. Gain mencakup seluruh sisa episode, termasuk perpindahan ulang.", "",
        "| Ukuran | " + " | ".join(NAMES[x] for x in labels) + " |", "|---|---:|---:|---:|"]
    def stat(d, field, key="mean"):
        value = d[field]; return f"{value[key]:.2f}" if value is not None else "-"
    for name, fmt in [
        ("Mean step pertama mencapai lantai lain", lambda d: stat(d, "first_arrival_step_reached_only")),
        ("Median step pertama mencapai lantai lain", lambda d: stat(d, "first_arrival_step_reached_only", "median")),
        ("Detik di lantai 1 / episode", lambda d: f"{d['mean_floor0_seconds_all']:.2f}"),
        ("Detik di lantai 2 / episode", lambda d: f"{d['mean_floor1_seconds_all']:.2f}"),
        ("Detik transit/tidak terklasifikasi", lambda d: f"{d['mean_transit_or_unclassified_seconds_all']:.2f}"),
        ("Detik sisa episode sesudah tiba (yang tiba)", lambda d: stat(d, "seconds_after_arrival_reached_only")),
        ("Detik tinggal di lantai baru sesudah tiba (yang tiba)", lambda d: stat(d, "new_floor_dwell_after_arrival_reached_only")),
        ("Tambahan coverage lantai 1 sesudah tiba", lambda d: f"{d['mean_floor0_gain_after_arrival_reached_only']*100:.2f} pp" if d['mean_floor0_gain_after_arrival_reached_only'] is not None else "-"),
        ("Tambahan coverage lantai 2 sesudah tiba", lambda d: f"{d['mean_floor1_gain_after_arrival_reached_only']*100:.2f} pp" if d['mean_floor1_gain_after_arrival_reached_only'] is not None else "-"),
        ("Tambahan coverage khusus lantai baru", lambda d: f"{d['mean_new_floor_gain_after_arrival_reached_only']*100:.2f} pp" if d['mean_new_floor_gain_after_arrival_reached_only'] is not None else "-"),
        ("Pertama tiba >=step 9.000", lambda d: str(d['late_arrival_9000_plus']['episodes'])),
        ("Tidak pernah tiba", lambda d: str(d['never_arrived']['episodes'])),
        ("Sudah tiba, tinggal di lantai baru <30 detik", lambda d: str(d['arrived_but_less_than30_seconds_on_new_floor'])),
        ("Gagal meski kedua lantai >=10% (selisih >10 pp)", lambda d: str(d['failed_both_floors_at_least10_but_difference_exceeds10pp']))]:
        lines.append("| " + name + " | " + " | ".join(fmt(total(x)) for x in labels) + " |")
    lines += ["", "## Konsistensi tiap seed", "",
        "| Seed | " + " | ".join(NAMES[x] + " sukses / tiba fisik" for x in labels) + " |", "|---|---:|---:|---:|"]
    for seed in SEEDS:
        values = [s["results"][x]["by_seed"][str(seed)] for x in labels]
        lines.append(f"| {seed} | " + " | ".join(f"{v['aggregate']['successes']}/10 / {v['physical_other_floor_reached']}/10" for v in values) + " |")
    lines += ["", "## Menang/kalah pada episode yang sama", "",
        "| A vs B | Hanya A sukses | Hanya B sukses | Keduanya | Keduanya gagal | Mean delta minimum coverage B-A |", "|---|---:|---:|---:|---:|---:|"]
    for key, result in s["paired"].items():
        a, b = key.split("__")
        lines.append(f"| {NAMES[a]} vs {NAMES[b]} | {result['only_a_success']} | {result['only_b_success']} | {result['both_success']} | {result['neither_success']} | {result['mean_minimum_floor_delta_b_minus_a']*100:+.2f} pp |")
    lines += ["", "Perbandingan step kedatangan pada pasangan yang KEDUA modelnya mencapai lantai lain (menghindari beda penyebut pada mean bersyarat):", "",
        "| A vs B | Keduanya tiba | Mean delta step kedatangan B-A |", "|---|---:|---:|"]
    for key, result in s["paired"].items():
        a, b = key.split("__"); delta = result["mean_first_arrival_step_delta_b_minus_a_common_arrivals"]
        value = f"{delta:+.2f}" if delta is not None else "-"
        lines.append(f"| {NAMES[a]} vs {NAMES[b]} | {result['both_physical_arrival_episodes']} | {value} |")
    # Describe, do not automatically replace/import a user's active model.
    ordered = sorted(labels, key=lambda x: (
        total(x)["aggregate"]["successes"], total(x)["physical_other_floor_reached"],
        total(x)["aggregate"]["minimum_floor"]), reverse=True)
    leader = ordered[0]
    lines += ["", "## Interpretasi untuk pemilihan checkpoint", "",
        f"Pada sampel ini, kandidat terkuat berdasarkan success, lalu ketercapaian lantai lain dan minimum coverage adalah **{NAMES[leader]}**. Ini ranking deskriptif, bukan jaminan generalisasi atau bukti signifikan statistik.",
        "Jangan memilih checkpoint terakhir hanya karena step-nya terbesar. Rata-rata waktu lantai atau coverage global yang mirip tidak berarti coverage seimbang dalam setiap episode.",
        "Lebih jarang mencapai lantai atas/bawah menunjukkan regresi pada tugas navigasi lintas lantai, tetapi tidak sendirinya membuktikan motor lupa cara menaiki tangga: model dapat gagal mencari, mendekati, atau memilih jalur tangga. Uji ini tidak merekam video/lintasan lengkap untuk membedakan penyebab itu.",
        "Reward split belum boleh dinyatakan lebih baik hanya dari TensorBoard; keputusan sementara harus mengikuti hasil berpasangan ini. Tidak ada model utama diganti, training dimulai, atau konfigurasi reward di-revert otomatis.", ""]
    lines += ["", "## Batas interpretasi", "",
        "- Ini hasil episode inference, bukan rerata ringkasan TensorBoard. Tidak membuktikan peningkatan signifikan statistik atau generalisasi ke semua map.",
        "- Lima seed lama adalah validasi pengembangan/pemilihan checkpoint, bukan uji akhir tesis independen. 50 episode berbagi lima map; jangan menganggapnya 50 map independen.",
        "- Episode sukses berhenti pada target 10% seimbang; coverage dan waktu tersensor oleh success, timeout/anti-spin, atau MaxStep. Ini bukan uji eksplorasi 100% tanpa batas.",
        "- Naik/turun fisik pada tabel berarti setidaknya sekali tiba di lantai lain dari kelompok spawn masing-masing. Total transfer adalah counter terkonfirmasi; trigger tangga dideduplikasi per trigger per episode, bukan semua kejadian masuk ulang.",
        "- Kurang dari 30 detik di lantai baru tidak otomatis buruk jika episode sudah sukses. Waktu/gain bersyarat arrival juga perlu dibaca bersama jumlah episode yang mencapai.",
        "- Revisit bisa dibutuhkan untuk kembali ke tangga. Nilai lebih rendah sendiri tidak selalu lebih baik.",
        "- Kedatangan fisik berbasis sampel physics-step; nilai -1 pada JSON/CSV berarti belum pernah tiba, bukan step nol. Tidak merekonstruksi lintasan kontinu sub-frame.",
        "- Error indeks UnityEditor.Search.SearchDatabase, jika ada, disimpan terpisah dalam diagnostics; error agent/inference menyebabkan run gagal. Baca AUDIT.json dan log untuk statusnya.",
        "- Tidak ada training baru, perubahan reward/curriculum, penyimpanan scene utama, atau pemilihan model otomatis berdasarkan reward saja.", ""]
    (directory/"HASIL_PERBANDINGAN.md").write_text("\n".join(lines), encoding="utf-8")
    return s


if __name__ == "__main__":
    parser = argparse.ArgumentParser(); parser.add_argument("directory", type=Path)
    args = parser.parse_args(); result = summarize(args.directory)
    print(json.dumps({k: v["overall"] for k, v in result["results"].items()}, indent=2))

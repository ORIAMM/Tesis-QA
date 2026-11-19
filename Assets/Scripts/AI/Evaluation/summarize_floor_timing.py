"""Read-only model/training analysis; writes diagnostic reports only, never trainer state."""
import argparse
import csv
import hashlib
import json
import math
import sys
from pathlib import Path
from statistics import mean, median

from summarize_coverage_validation import aggregate

MODEL_HASH = "a6a6410d44a5bffa810553e1f0baa3b9fe4b699a2c8d73c32eb00c6c32d7a9a2"
SEEDS = [1427003 + i * 7919 for i in range(5)]


def validate(report, *, model="Phase3R_2000063", digest=MODEL_HASH,
             protocol="phase3r-physical-floor-timing-level8-v1", revision=1):
    if (report.get("error") or report["protocol"] != protocol
        or report["model"] != model or report["modelSha256"] != digest
        or report["requestedEpisodes"] != 50 or len(report["episodes"]) != 50
        or report["maxStep"] != 12000 or report["rewardRevision"] != revision
        or report["mapRegenerationInterval"] != 10 or report["spawnSeed"] != 27421
        or report["inferenceSeed"] != 58231 or not report["deterministicInference"]
        or report["timeScale"] != 40 or abs(report["fixedDeltaTime"] - .02) > 1e-6
        or abs(report["coverageTarget"] - .1) > 1e-6
        or abs(report["maximumFloorDifference"] - .1) > 1e-6):
        raise ValueError("Incomplete or changed evaluation protocol/model.")
    for i, entry in enumerate(report["episodes"]):
        e = entry["episode"]; r = e["result"]; t = r["floorTiming"]
        if (e["episode"] != i+1 or e["mapSeed"] != SEEDS[i//10] or r["startFloor"] not in (0, 1)
            or not 0 < r["steps"] <= 12000 or r["lesson"] != 4 or r["stairPracticeGuided"]
            or r["stairRehearsal"] or r["searchShapingActive"]):
            raise ValueError("Invalid episode, seed, steps, or unexpected guidance.")
        if any(not math.isfinite(v) for v in t.values()):
            raise ValueError("Non-finite timing.")
        if sum(t[k] for k in ("floor0Steps", "floor1Steps", "transitOrUnclassifiedSteps")) != r["steps"]:
            raise ValueError("Occupancy steps do not sum to episode steps.")
        if any(t[k] < 0 for k in ("floor0Steps", "floor1Steps", "transitOrUnclassifiedSteps")):
            raise ValueError("Negative occupancy.")
        for prefix in ("floor0", "floor1", "transitOrUnclassified"):
            if abs(t[prefix+"Seconds"] - t[prefix+"Steps"]*report["fixedDeltaTime"]) > 2e-4:
                raise ValueError("Incorrect simulated time conversion.")
        if abs(t["elapsedSeconds"] - r["steps"]*report["fixedDeltaTime"]) > 2e-4:
            raise ValueError("Incorrect elapsed time.")
        coverage = [r["floorZeroCoverage"], r["floorOneCoverage"]]
        if any(not math.isfinite(v) or not 0 <= v <= 1 for v in coverage):
            raise ValueError("Invalid final coverage.")
        if t["firstOtherFloorStep"] >= 0:
            step = t["firstOtherFloorStep"]
            if not 0 < step <= r["steps"] or t["firstOtherFloorIndex"] != 1-r["startFloor"]:
                raise ValueError("Invalid first physical arrival.")
            if abs(t["firstOtherFloorSeconds"] - step*.02) > 2e-4:
                raise ValueError("Incorrect arrival seconds.")
            if abs(t["secondsAfterFirstTransfer"] - (r["steps"]-step)*.02) > 2e-4:
                raise ValueError("Incorrect remaining time after arrival.")
            for floor, final in enumerate(coverage):
                before = t[f"floor{floor}CoverageAtTransfer"]; gain = t[f"floor{floor}CoverageGainAfterTransfer"]
                if not 0 <= before <= final+1e-6 or abs(gain-(final-before)) > 1e-6:
                    raise ValueError("Incorrect coverage snapshot/gain.")
            if sum(t[f"floor{f}SecondsAfterTransfer"] for f in (0, 1)) > t["secondsAfterFirstTransfer"]+2e-4:
                raise ValueError("Post-transfer dwell exceeds remaining episode time.")
        elif (t["firstOtherFloorStep"] != -1 or t["firstOtherFloorIndex"] != -1
              or t[f"floor{1-r['startFloor']}Steps"] != 0
              or any(t[k] != -1 for k in ("firstOtherFloorSeconds", "secondsAfterFirstTransfer",
                  "floor0CoverageAtTransfer", "floor1CoverageAtTransfer",
                  "floor0CoverageGainAfterTransfer", "floor1CoverageGainAfterTransfer"))):
            raise ValueError("Never-transferred episode must use missing-value sentinels.")
        if r["success"] and (min(coverage) < .1-1e-5 or abs(coverage[0]-coverage[1]) > .1+1e-5):
            raise ValueError("Success does not meet coverage criterion.")


def describe(entries):
    episodes = [x["episode"] for x in entries]
    reached = [e for e in episodes if e["result"]["floorTiming"]["firstOtherFloorStep"] >= 0]
    timing = [e["result"]["floorTiming"] for e in episodes]
    def stats(values):
        return {"mean": mean(values), "median": median(values), "min": min(values), "max": max(values)} if values else None
    late = [e for e in reached if e["result"]["floorTiming"]["firstOtherFloorStep"] >= 9000]
    early = [e for e in reached if e not in late]
    failed = [e for e in episodes if not e["result"]["success"]]
    def subgroup(rows):
        return {"episodes": len(rows), "successes": sum(e["result"]["success"] for e in rows)}
    return {"aggregate": aggregate(episodes), "physical_other_floor_reached": len(reached),
        "first_arrival_step_reached_only": stats([e["result"]["floorTiming"]["firstOtherFloorStep"] for e in reached]),
        "seconds_after_arrival_reached_only": stats([e["result"]["floorTiming"]["secondsAfterFirstTransfer"] for e in reached]),
        "mean_floor0_seconds_all": mean(t["floor0Seconds"] for t in timing),
        "mean_floor1_seconds_all": mean(t["floor1Seconds"] for t in timing),
        "mean_transit_or_unclassified_seconds_all": mean(t["transitOrUnclassifiedSeconds"] for t in timing),
        "new_floor_dwell_after_arrival_reached_only": stats([
            e["result"]["floorTiming"][f"floor{1-e['result']['startFloor']}SecondsAfterTransfer"] for e in reached]),
        "mean_floor0_gain_after_arrival_reached_only": mean(e["result"]["floorTiming"]["floor0CoverageGainAfterTransfer"] for e in reached) if reached else None,
        "mean_floor1_gain_after_arrival_reached_only": mean(e["result"]["floorTiming"]["floor1CoverageGainAfterTransfer"] for e in reached) if reached else None,
        "mean_new_floor_gain_after_arrival_reached_only": mean(e["result"]["floorTiming"][f"floor{1-e['result']['startFloor']}CoverageGainAfterTransfer"] for e in reached) if reached else None,
        "late_arrival_9000_plus": subgroup(late), "earlier_arrival_under9000": subgroup(early),
        "failed_both_floors_at_least10_but_difference_exceeds10pp": sum(
            min(e["result"]["floorZeroCoverage"], e["result"]["floorOneCoverage"]) >= .1
            and abs(e["result"]["floorZeroCoverage"]-e["result"]["floorOneCoverage"]) > .1 for e in failed),
        "arrived_but_less_than30_seconds_on_new_floor": sum(
            e["result"]["floorTiming"][f"floor{1-e['result']['startFloor']}SecondsAfterTransfer"] < 30 for e in reached),
        "never_arrived": subgroup([e for e in episodes if e not in reached])}


def training_summary(root):
    from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
    sys.path.insert(0, str(root/"Assets/Scripts/AI/Training"))
    from terrain_curriculum import TerrainWindow
    run = root/"results/Phase3R"; s = json.loads((run/"run_logs/training_status.json").read_text())
    w = TerrainWindow.restore(s["phase3_terrain"], 8)
    data = {}
    for file in sorted((run/"Explorer").glob("events.out*")):
        a = EventAccumulator(str(file), size_guidance={"scalars": 0}); a.Reload()
        for tag in a.Tags()["scalars"]:
            dest = data.setdefault(tag, {})
            for event in a.Scalars(tag):
                if event.step not in dest or event.wall_time > dest[event.step].wall_time:
                    dest[event.step] = event
    windows = {}
    tags = ["Environment/Cumulative Reward", "AI/Success", "AI/Floor 0 Coverage", "AI/Floor 1 Coverage",
        "AI/Minimum Floor Coverage", "AI/Episode Steps", "AI/Other Floor Reached", "AI/Steps To Other Floor",
        "AI/Ascent Transitions", "AI/Descent Transitions", "AI/Step Limit", "AI/No New Area Timeout"]
    for label, low, high in [("first500k", 0, 500000), ("last500k", 1500000, 2000100), ("last100k", 1900000, 2000100)]:
        windows[label] = {}
        for tag in tags:
            values = [e.value for e in data.get(tag, {}).values() if low < e.step <= high]
            windows[label][tag] = {"mean": mean(values) if values else None, "summaries": len(values)}
    return {"final_step": s["Explorer"]["final_checkpoint"]["steps"],
        "terrain_level": 8, "last100_exact_mastery": w.summary(),
        "window_statistic": "Unweighted mean of TensorBoard 10k-step summary means, not exact episode-weighted mean",
        "existing_steps_to_other_floor_warning": "Trigger/visited-floor milestone, includes -1 when not reached; NOT physical timing requested here",
        "windows": windows}


def summarize(directory, root):
    directory, root = Path(directory), Path(root)
    if (directory/"timing2000063.done").read_text() != "complete": raise ValueError("Incomplete evaluation.")
    report = json.loads((directory/"timing2000063.json").read_text(encoding="utf-8-sig")); validate(report)
    if hashlib.sha256((root/"results/Phase3R/Explorer.onnx").read_bytes()).hexdigest() != MODEL_HASH:
        raise ValueError("Source model changed.")
    s = {"protocol": report["protocol"], "model": report["model"], "model_sha256": MODEL_HASH,
        "training": False, "not_independent_thesis_test": True,
        "timing": describe(report["episodes"]),
        "by_seed": {str(seed): describe([e for e in report["episodes"] if e["episode"]["mapSeed"] == seed]) for seed in SEEDS},
        "by_start_floor": {str(f): describe([e for e in report["episodes"] if e["episode"]["result"]["startFloor"] == f]) for f in (0, 1)},
        "training_summary": training_summary(root), "editor_diagnostics": report.get("diagnostics", [])}
    (directory/"summary.json").write_text(json.dumps(s, indent=2), encoding="utf-8")
    rows = []
    for entry in report["episodes"]:
        e = entry["episode"]; r = e["result"]; t = r["floorTiming"]
        rows.append({"episode": e["episode"], "seed": e["mapSeed"], "start_floor_index": r["startFloor"],
            "success": r["success"], "episode_steps": r["steps"], **t,
            "final_floor0_coverage": r["floorZeroCoverage"], "final_floor1_coverage": r["floorOneCoverage"]})
    with (directory/"PER_EPISODE.csv").open("w", encoding="utf-8-sig", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=list(rows[0])); writer.writeheader(); writer.writerows(rows)
    d = s["timing"]; a = d["aggregate"]; tr = s["training_summary"]; first = d["first_arrival_step_reached_only"]
    lines = ["# Phase3R: waktu perpindahan lantai dan coverage", "",
        "Model terakhir: 2.000.063 step fine-tuning (diinisialisasi dari Phase3 6.000.091).",
        "Uji BARU, inference saja: 50 episode, lima seed pengembangan, 10 per seed; model deterministik, speed 6, 40x, MaxStep 12.000, dt 0,02 detik.",
        "Level 8: dua lantai 24x24, target >=10% tiap lantai dan selisih <=10 poin persentase; reward/timeout revision 1.", "",
        "## Hasil training tersimpan", "",
        f"Budget selesai pada {tr['final_step']:,} step. Masih level 8; sukses persis {tr['last100_exact_mastery']['success']:.0%} pada 100 episode terakhir.",
        "Ringkasan TensorBoard berikut adalah rata-rata tak berbobot dari ringkasan 10.000 step, BUKAN rerata episode persis:", "",
        "| Metrik | 500k awal | 500k akhir |", "|---|---:|---:|"]
    for tag in ("AI/Success", "AI/Floor 0 Coverage", "AI/Floor 1 Coverage", "AI/Minimum Floor Coverage", "AI/Episode Steps", "AI/Other Floor Reached", "AI/Step Limit"):
        x = tr["windows"]["first500k"][tag]["mean"]; y = tr["windows"]["last500k"][tag]["mean"]
        fmt = (lambda v: f"{v:.0f}") if tag == "AI/Episode Steps" else (lambda v: f"{v:.1%}")
        lines.append(f"| {tag} | {fmt(x)} | {fmt(y)} |")
    lines += ["", "Peningkatan perpindahan lantai belum disertai peningkatan konsisten minimum coverage/sukses. Reward total tidak layak dibandingkan langsung dengan Phase3 lama karena rumusnya berbeda.",
        "Log training lama tidak memuat dwell time atau coverage-at-transfer per episode; tabel berikut berasal dari uji inference baru, bukan rekonstruksi episode training.", "",
        "## Tiga ukuran yang diminta", "",
        "Step kedatangan dihitung DI DALAM episode (1-12.000), bukan akumulasi step training. Satu step = physics step, bukan satu keputusan policy (DecisionPeriod 5).",
        f"Sukses: {a['successes']}/50. Mencapai lantai lain secara fisik: {d['physical_other_floor_reached']}/50.",
        f"Pertama mencapai lantai lain (hanya yang mencapai): mean {first['mean']:.0f}, median {first['median']:.0f}, rentang {first['min']}-{first['max']} step.",
        f"Waktu rata-rata seluruh episode: lantai 1 {d['mean_floor0_seconds_all']:.2f} detik; lantai 2 {d['mean_floor1_seconds_all']:.2f} detik; transit/tidak terklasifikasi {d['mean_transit_or_unclassified_seconds_all']:.2f} detik.",
        f"Coverage tambahan setelah perpindahan pertama (hanya episode yang mencapai): lantai 1 +{d['mean_floor0_gain_after_arrival_reached_only']*100:.2f} pp, lantai 2 +{d['mean_floor1_gain_after_arrival_reached_only']*100:.2f} pp.",
        f"Khusus lantai yang baru dicapai: tambahan coverage rata-rata +{d['mean_new_floor_gain_after_arrival_reached_only']*100:.2f} pp; waktu tinggal sesudah tiba rata-rata {d['new_floor_dwell_after_arrival_reached_only']['mean']:.2f} detik.", "",
        "### Pemeriksaan hipotesis terlambat menemukan tangga", "",
        f"- Pertama tiba >=step 9.000 (75% budget): {d['late_arrival_9000_plus']['episodes']} episode, sukses {d['late_arrival_9000_plus']['successes']}.",
        f"- Pertama tiba sebelum step 9.000: {d['earlier_arrival_under9000']['episodes']} episode, sukses {d['earlier_arrival_under9000']['successes']}.",
        f"- Tidak pernah tiba: {d['never_arrived']['episodes']} episode.",
        f"- Sudah tiba tetapi waktu tinggal di lantai baru <30 detik: {d['arrived_but_less_than30_seconds_on_new_floor']} episode.",
        f"- Gagal meskipun kedua lantai >=10%, karena selisihnya >10 pp: {d['failed_both_floors_at_least10_but_difference_exceeds10pp']} episode.",
        "Ini diagnosis korelasional: tiba awal tetapi coverage rendah tidak dapat dijelaskan hanya oleh kehabisan waktu sebelum transfer. Lihat waktu tinggal dan gains tiap episode.", "",
        "## Konsistensi seed", "", "| Seed | Sukses | Tiba lantai lain | Mean step pertama (yang tiba) |", "|---|---:|---:|---:|"]
    for seed, v in s["by_seed"].items():
        f = v["first_arrival_step_reached_only"]
        lines.append(f"| {seed} | {v['aggregate']['successes']}/10 | {v['physical_other_floor_reached']}/10 | {f['mean']:.0f} |" if f else f"| {seed} | 0/10 | 0/10 | - |")
    lines += ["", "## Catatan per episode", "",
        "L1/L2 = lantai bawah/atas (index kode 0/1). Waktu dalam detik simulasi, bukan detik dinding pada 40x. Gain dalam poin persentase, bukan persen relatif.", "",
        "| Ep | Mulai | Total step | Step pertama lantai lain | Detik L1 | Detik L2 | Transit/tidak pasti | Gain L1 (pp) | Gain L2 (pp) |", "|---|---|---:|---:|---:|---:|---:|---:|---:|"]
    for r in rows:
        reached = r["firstOtherFloorStep"] >= 0
        gains = [f"{r[f'floor{f}CoverageGainAfterTransfer']*100:.2f}" if reached else "-" for f in (0, 1)]
        lines.append(f"| {r['episode']} | L{r['start_floor_index']+1} | {r['episode_steps']} | {r['firstOtherFloorStep'] if reached else '-'} | {r['floor0Seconds']:.2f} | {r['floor1Seconds']:.2f} | {r['transitOrUnclassifiedSeconds']:.2f} | {gains[0]} | {gains[1]} |")
    lines += ["", "## Definisi dan batas pengukuran", "",
        "- Kedatangan fisik: sampel pasca-Motor.Move pertama saat grounded, ground probe normal <=10 derajat, dan ketinggian kaki <=0,35 unit dari bidang lantai WFC lain. Bukan sekadar trigger atau tag Stair.",
        "- Waktu lantai = jumlah physics-step yang memenuhi klasifikasi di atas x fixedDeltaTime. Ramp, udara, atau ground yang belum jelas dipisahkan. Jumlah L1+L2+transit tepat sama dengan durasi episode; resolusi 0,02 detik. Bukan pengukuran kontinu sub-frame.",
        "- Snapshot coverage diambil ketika kedatangan fisik pertama terukur. Trigger tile masuk yang diproses sesudah sampel itu bisa tercakup dalam gain setelah transfer; definisinya konsisten.",
        "- Step pertama tidak direset oleh perpindahan berikutnya. Tidak pernah tiba ditandai -1 dalam JSON/CSV, bukan step nol. Statistik arrival/gain mengecualikan episode itu; dwell seluruh episode tetap memasukkannya.",
        "- Gain = coverage final dikurangi coverage pada kedatangan pertama, per lantai, termasuk perpindahan ulang berikutnya. Nilai coverage adalah proporsi trigger tile unik, bukan luas permukaan mesh yang terlihat.",
        "- Episode sukses berakhir saat ambang tercapai. Waktu dan coverage karenanya tersensor oleh keberhasilan, timeout, anti-spin, atau MaxStep; bukan coverage maksimum yang sanggup dicapai tanpa batas.",
        "- Lima seed ini validasi pengembangan yang pernah dipakai, bukan uji akhir tesis independen. Tidak ada baseline baru berpasangan dengan reward revision 1 dalam uji ini; jangan membandingkan sebab-akibat dengan hasil revision 0.",
        "- Tidak ada training baru, perubahan reward/observasi/curriculum, atau penyimpanan scene utama. Telemetri menambah metrik, bukan memberi petunjuk tambahan kepada PPO.", ""]
    example = next((r for r in rows if r["episode"] == 23), None)
    if example:
        lines += ["## Contoh pembacaan: episode 23", "",
            f"Mulai lantai 1, pertama tiba di lantai 2 pada step {example['firstOtherFloorStep']}; masih tersisa {example['secondsAfterFirstTransfer']:.2f} detik simulasi sampai episode selesai.",
            f"Namun waktu di lantai 2 sesudah itu hanya {example['floor1SecondsAfterTransfer']:.2f} detik; tambahan coverage lantai 2 {example['floor1CoverageGainAfterTransfer']*100:.2f} pp, dibanding lantai 1 {example['floor0CoverageGainAfterTransfer']*100:.2f} pp.",
            "Pola ini lebih sesuai dengan kembali terlalu cepat/menghabiskan waktu di lantai lama daripada semata-mata terlambat menemukan tangga. Ini contoh perilaku terukur, bukan diagnosis pasti isi policy.", ""]
    (directory/"HASIL_TIMING.md").write_text("\n".join(lines), encoding="utf-8")
    return s


if __name__ == "__main__":
    p = argparse.ArgumentParser(); p.add_argument("directory", type=Path); p.add_argument("--root", type=Path, default=Path.cwd())
    args = p.parse_args(); print(json.dumps(summarize(args.directory, args.root)["timing"], indent=2))

"""Summarize the bounded, inference-only Phase2E small/large coverage validation.

JSON only, no trainer, no model selection, no checkpoint or scene changes.
Rejects incomplete runs and records actual seeds (including generation fallbacks).
"""
import argparse
import hashlib
import json
from pathlib import Path
from statistics import mean, median

MODEL_SHA256 = "9fc8ea95e62797e8460f5069ba3d073c208ef5295c8c5ad6c4aa5cfd7ffba026"
EXPECTED_SEEDS = [913701 + i * 7919 for i in range(5)]


def aggregate(episodes):
    rows = [e["result"] for e in episodes]
    def reached(floor):
        group = [r for r in rows if r["startFloor"] == floor]
        key = "ascentTransitions" if floor == 0 else "descentTransitions"
        return {"episodes": len(group), "reached": sum(r[key] > 0 for r in group)}
    return {"episodes": len(rows), "successes": sum(r["success"] for r in rows),
        "floor0": mean(r["floorZeroCoverage"] for r in rows),
        "floor1": mean(r["floorOneCoverage"] for r in rows),
        "minimum_floor": mean(min(r["floorZeroCoverage"], r["floorOneCoverage"]) for r in rows),
        "floor_difference": mean(abs(r["floorZeroCoverage"]-r["floorOneCoverage"]) for r in rows),
        "other_floor_reached": sum(r["ascentTransitions"]+r["descentTransitions"] > 0 for r in rows),
        "ascent": reached(0), "descent": reached(1),
        "stair_entries": sum(r["stairTraversals"] for r in rows),
        "ascent_transitions": sum(r["ascentTransitions"] for r in rows),
        "descent_transitions": sum(r["descentTransitions"] for r in rows),
        "mean_steps": mean(r["steps"] for r in rows), "median_steps": median(r["steps"] for r in rows),
        "mean_revisits": mean(r["revisits"] for r in rows),
        "mean_short_loops": mean(r["shortLoops"] for r in rows),
        "mean_collisions": mean(r["collisions"] for r in rows),
        "timeout_episodes": sum(r["noNewAreaTimeouts"] > 0 for r in rows),
        "stationary_episodes": sum(r["noDisplacementFailures"] > 0 for r in rows),
        "step_limit_episodes": sum(r["stepLimits"] > 0 for r in rows)}


def summarize(directory):
    directory = Path(directory)
    reports, results = {}, {}
    for condition, count, interval, maximum in [("small",30,6,6000),("large",10,2,7500)]:
        report = json.loads((directory/f"{condition}.json").read_text(encoding="utf-8-sig"))
        if report.get("error") or (directory/f"{condition}.done").read_text() != "complete":
            raise ValueError(f"Incomplete/error run: {condition}: {report.get('error')}")
        if report["requestedEpisodes"] != count or len(report["episodes"]) != count:
            raise ValueError(f"Wrong episode count: {condition}")
        if report["modelSha256"] != MODEL_SHA256 or not report["deterministicInference"]:
            raise ValueError("Wrong model or inference mode.")
        if (report["mapRegenerationInterval"] != interval or report["maxStep"] != maximum
            or abs(report["coverageTarget"]-0.2)>1e-6 or abs(report["maximumFloorDifference"]-0.1)>1e-6
            or report["spawnSeed"] != 27421 or report["inferenceSeed"] != 58231):
            raise ValueError("Changed protocol.")
        seeds = sorted({e["mapSeed"] for e in report["episodes"]})
        if len(seeds) != 5 or any(sum(e["mapSeed"]==s for e in report["episodes"])!=interval for s in seeds):
            raise ValueError(f"Expected five fixed map batches: {condition}: {seeds}")
        for e in report["episodes"]:
            r = e["result"]
            if r["stairPracticeGuided"] or r["searchShapingActive"] or r["stairRehearsal"] or r["lesson"] != 4:
                raise ValueError("Evaluation unexpectedly contains transfer/guidance, rather than coverage.")
            if r["steps"] > maximum or any(not 0 <= r[k] <= 1 for k in ["floorZeroCoverage","floorOneCoverage"]):
                raise ValueError("Invalid steps or coverage.")
            if r["success"] and (min(r["floorZeroCoverage"],r["floorOneCoverage"]) < 0.2-1e-5
                or abs(r["floorZeroCoverage"]-r["floorOneCoverage"])>0.1+1e-5):
                raise ValueError("Success violates per-episode balanced coverage criterion.")
        results[condition] = {"aggregate":aggregate(report["episodes"]),
            "actual_seeds":seeds,"seed_schedule_exact":seeds==EXPECTED_SEEDS,
            "by_seed":{str(s):aggregate([e for e in report["episodes"] if e["mapSeed"]==s]) for s in seeds}}
        reports[condition] = report
    summary = {"model":"Phase2E_7000033","model_sha256":MODEL_SHA256,
        "protocol":"coverage-two-floor-fixed-level9-v1","training":False,
        "not_independent_thesis_test":True,"not_paired_across_map_sizes":True,
        "results":results,
        "editor_only_diagnostics":{c:reports[c].get('diagnostics',[]) for c in reports},
        "large_scene_notes":["24x24 vs 10x10 cells per floor; two floors",
            "7500 vs 6000 physics-step episode limit",
            "Phase3 scene's existing timeout/revisit settings retained",
            "coverage target temporarily standardized to 20% per floor, difference <=10 percentage points",
            "no large-map training performed; this is zero-shot transfer screening"]}
    (directory/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
    lines=['# Uji inference model Phase2E 7.000.033','',
        'Tanpa training; model deterministik, speed 6, time scale 30x. Lima seed per kondisi.',
        'Map kecil 10x10x2: 30 episode (6 per seed). Map besar 24x24x2: 10 episode (2 per seed).',
        'Target kedua kondisi: coverage >=20% setiap lantai dan selisih <=10 poin persentase dalam episode yang sama.',
        'Ini validasi pengembangan/transfer awal, bukan uji akhir tesis independen atau perbandingan ukuran map yang mengisolasi semua variabel.','',
        '| Metrik | Map kecil | Map besar (zero-shot) |','|---|---:|---:|']
    small=results['small']['aggregate']; large=results['large']['aggregate']
    for label,key,fmt in [('Berhasil','successes','count'),('Mencapai lantai lain','other_floor_reached','count'),
        ('Coverage bawah','floor0','percent'),('Coverage atas','floor1','percent'),
        ('Minimum coverage per episode','minimum_floor','percent'),('Selisih coverage per episode','floor_difference','pp'),
        ('Trigger tangga unik per episode (total)','stair_entries','int'),('Transfer naik','ascent_transitions','int'),
        ('Transfer turun','descent_transitions','int'),('Rata-rata step','mean_steps','float'),
        ('Rata-rata revisit','mean_revisits','float'),('Rata-rata short-loop','mean_short_loops','float'),
        ('Episode timeout tanpa area baru','timeout_episodes','count'),('Episode mencapai batas step','step_limit_episodes','count')]:
        def value(x):
            v=x[key]
            if fmt=='count':return f"{v}/{x['episodes']} ({v/x['episodes']:.1%})"
            if fmt=='percent':return f'{v:.1%}'
            if fmt=='pp':return f'{v*100:.1f} pp'
            if fmt=='float':return f'{v:.2f}'
            return str(v)
        lines.append(f'| {label} | {value(small)} | {value(large)} |')
    lines += ['','## Konsistensi tiap seed','','| Kondisi | Seed aktual | Berhasil | Spawn bawah/atas |','|---|---:|---:|---:|']
    for c in ['small','large']:
        for seed,s in results[c]['by_seed'].items():
            lines.append(f"| {c} | {seed} | {s['successes']}/{s['episodes']} | {s['ascent']['episodes']}/{s['descent']['episodes']} |")
    lines += ['','## Batas interpretasi','',
        '- Pergantian lantai dihitung dari perpindahan yang terkonfirmasi, bukan sekadar masuk trigger tangga.',
        '- Hitungan trigger tangga dideduplikasi per trigger per episode; bukan semua kejadian masuk ulang.',
        '- Episode sukses berhenti saat mencapai ambang 20% seimbang; ini bukan pengujian coverage maksimum 100%.',
        '- Revisit dapat diperlukan untuk kembali ke tangga. Revisit bukan otomatis bug atau kegagalan.',
        '- Map besar memakai MaxStep/timeout scene Phase 3 yang berbeda. Perbedaan hasil bukan semata-mata efek ukuran.',
        '- Seed/spawn/yaw aktual tersimpan per episode; spawn tidak dipaksa seimbang. Baca jumlah episode per kelompok.',
        '- Lima seed dan sedikit episode map besar belum memberi kepastian generalisasi/statistik.',
        '- Model ini belum dilatih pada map besar. Hasil besar rendah tidak membatalkan keputusan pengguna untuk fine-tuning Phase 3.',
        '- Angka training memakai sampling aksi dan map berbeda; hasil deterministik ini bukan pembuktian penurunan policy dari 75% ke 33%.',
        '- Tercatat diagnostik indeks pencarian editor Unity (SearchDatabase); bukan error inference/agent. Detail ada di JSON/log.',
        '- Tidak ada reward, collider, checkpoint atau scene utama yang diubah oleh evaluasi.']
    (directory/'HASIL_UJI.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    return summary


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('directory');args=p.parse_args()
    print(json.dumps(summarize(args.directory),indent=2))

"""One bounded training job followed by paired evaluation and model publication.

Runs in the main project root; Unity uses only its isolated .ai-eval snapshot.
No installed packages, original scenes, rewards or baseline checkpoints are edited.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import sys
import time

import yaml

ROOT = Path(__file__).resolve().parents[4]
UNITY = Path("C:/Program Files/Unity/Hub/Editor/6000.5.4f1/Editor/Unity.exe")


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write_json(path, value):
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(value, indent=2), encoding="utf-8")
    temporary.replace(path)


def hidden_options():
    if os.name != "nt":
        return {}
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    return {"startupinfo": startup, "creationflags": subprocess.CREATE_NO_WINDOW}


def choose_steps(available, final):
    if not available:
        raise ValueError("No checkpoint pairs to evaluate")
    return sorted({min(available, key=lambda s: abs(s - final * fraction)) for fraction in (0.5, 0.9, 1.0)})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True)
    parser.add_argument("--run-id", default="Phase2M5")
    parser.add_argument("--operations", required=True)
    parser.add_argument("--port", type=int, default=5017)
    parser.add_argument("--resume", action="store_true")
    parser.add_argument("--validation-only", action="store_true")
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9_-]+", args.run_id):
        raise ValueError("Simple run ID required")
    if args.validation_only and not args.run_id.startswith("HeadlessValidation"):
        raise ValueError("Validation-only is reserved for a separate test ID")
    if Path.cwd().resolve() != ROOT:
        raise ValueError("Launch from the project root")
    isolated = ROOT / ".ai-eval"
    operations = Path(args.operations).resolve()
    if not operations.is_relative_to(ROOT / "results/HeadlessRuns") or operations.exists():
        raise ValueError("Choose a new operations folder inside results/HeadlessRuns")
    if isolated.is_symlink() or (isolated / "Assets").is_symlink():
        raise ValueError("The isolated project must not link to main assets")
    config = Path(args.config).resolve()
    settings = yaml.safe_load(config.read_text(encoding="utf-8-sig"))
    maximum = float(settings["behaviors"]["Explorer"]["max_steps"])
    run = ROOT / "results" / args.run_id
    if not args.resume and run.exists():
        raise ValueError("Run exists; refusing to overwrite it")
    if args.resume and not (run / "Explorer/checkpoint.pt").is_file():
        raise ValueError("No checkpoint to resume")
    with socket.socket() as probe:
        probe.bind(("localhost", args.port))
    lock_path = isolated / "AIHeadlessRun.lock"
    lock = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    os.write(lock, json.dumps({"pid": os.getpid(), "run_id": args.run_id}).encode())
    operations.mkdir(parents=True)
    if args.validation_only:
        # Transport/lifecycle test only, never used by the actual Phase2M5 job.
        behavior = settings["behaviors"]["Explorer"]
        behavior.update(max_steps=1500, summary_freq=500, checkpoint_interval=500)
        behavior["hyperparameters"].update(batch_size=128, buffer_size=1024)
        config = operations / "validation-config.yaml"
        config.write_text(yaml.safe_dump(settings, sort_keys=False), encoding="utf-8")
        maximum = 1500
    status = {"run_id": args.run_id, "pid": os.getpid(), "max_steps": maximum,
              "operations": str(operations), "port": args.port, "stage": "preparing"}
    status_file = operations / "pipeline_status.json"
    scene = ROOT / "Assets/Scenes/AI/AI_Phase2_SmallWFC.unity"
    baseline = ROOT / "results/Phase2M4/Explorer/checkpoint.pt"
    protected = {str(path): digest(path) for path in (scene, baseline)}
    status["protected_hashes"] = protected
    processes = []
    logs = []

    def update(stage, **extra):
        status.update(stage=stage, utc=datetime.now(timezone.utc).isoformat(), **extra)
        write_json(status_file, status)
        print(stage, extra, flush=True)

    def launch(command, label, cwd):
        output = open(operations / f"{label}.out.log", "w", encoding="utf-8")
        error = open(operations / f"{label}.err.log", "w", encoding="utf-8")
        logs.extend((output, error))
        process = subprocess.Popen(command, cwd=cwd, stdout=output, stderr=error, **hidden_options())
        processes.append(process)
        return process

    try:
        update("starting_trainer")
        trainer_command = [sys.executable, "-u", "-B", str(ROOT / "Assets/Scripts/AI/Training/train_phase2_headless.py"),
                           str(config), f"--run-id={args.run_id}", f"--editor-port={args.port}",
                           "--torch-device=cpu", "--timeout-wait=180"]
        if args.resume:
            trainer_command.append("--resume")
        trainer = launch(trainer_command, "trainer", ROOT)
        time.sleep(3)
        if trainer.poll() is not None:
            raise RuntimeError("Trainer exited before Unity launch; inspect trainer logs")
        unity_command = [str(UNITY), "-batchmode", "-nographics", "-projectPath", str(isolated),
                         "-executeMethod", "TesisQA.AI.Editor.AIModelEvaluationCommand.RunHeadlessTraining",
                         "-aiHeadlessStatus", str(operations), "--mlagents-port", str(args.port),
                         "-logFile", str(operations / "unity-training.log")]
        editor = launch(unity_command, "unity-launch", isolated)
        update("training", trainer_pid=trainer.pid, unity_pid=editor.pid)
        started = time.monotonic()
        while trainer.poll() is None:
            if editor.poll() is not None:
                raise RuntimeError(f"Isolated Unity exited during training: {editor.returncode}")
            if (operations / "unity_failed.txt").exists():
                raise RuntimeError((operations / "unity_failed.txt").read_text()[:3000])
            if time.monotonic() - started > 24 * 3600:
                raise RuntimeError("Training exceeded the 24-hour safety limit; no automatic budget extension")
            time.sleep(5)
        if trainer.returncode != 0:
            raise RuntimeError(f"Trainer exited with code {trainer.returncode}; inspect logs")
        native = json.loads((run / "run_logs/training_status.json").read_text(encoding="utf-8-sig"))
        final = native["Explorer"]["final_checkpoint"]["steps"]
        if final < maximum:
            raise RuntimeError(f"Trainer stopped early at {final}/{maximum}")
        (operations / "stop.request").write_text("trainer completed normally", encoding="utf-8")
        editor.wait(timeout=90)
        if editor.returncode != 0:
            raise RuntimeError(f"Unity failed while shutting down: {editor.returncode}")
        update("training_complete", final_step=final,
               final_mixed_level=native.get("phase2_mixed_search", {}).get("stage"))
        if args.validation_only:
            update("validation_complete")
            return

        evaluation = run / "headless_evaluation"
        models = evaluation / "models"
        models.mkdir(parents=True, exist_ok=False)
        available = sorted(int(p.stem.split("-")[-1]) for p in (run / "Explorer").glob("Explorer-*.pt")
                           if p.with_suffix(".onnx").is_file())
        selected_steps = choose_steps(available, final)
        labels = ["Phase2M4_15000072"] + [f"{args.run_id}_{step}" for step in selected_steps]
        for label in labels:
            if label == "Phase2M4_15000072":
                source_base = ROOT / "results/Phase2_initialization/Phase2M4-15000072"
            else:
                source_base = run / "Explorer" / f"Explorer-{label.rsplit('_', 1)[1]}"
            for suffix in (".onnx", ".pt"):
                source = source_base.with_suffix(suffix)
                target = models / (label + suffix)
                shutil.copy2(source, target)
            shutil.copy2(models / (label + ".onnx"), isolated / "Assets/Scripts/AI/Evaluation/Models" / (label + ".onnx"))
        update("evaluating", evaluation_directory=str(evaluation), evaluation_models=labels)
        evaluate_command = [str(UNITY), "-batchmode", "-nographics", "-projectPath", str(isolated),
                            "-executeMethod", "TesisQA.AI.Editor.AIModelEvaluationCommand.RunComparison",
                            "-aiEvaluationModels", ",".join(labels), "-aiEvaluationEpisodes", "50",
                            "--mlagents-port", "-1", "-aiEvaluationOutput", str(evaluation),
                            "-logFile", str(evaluation / "unity-evaluation.log")]
        evaluator = launch(evaluate_command, "evaluation-launch", isolated)
        update("evaluating", evaluation_pid=evaluator.pid)
        evaluator.wait(timeout=5 * 3600)
        if evaluator.returncode != 0:
            raise RuntimeError(f"Evaluation exited with code {evaluator.returncode}")
        sys.path.insert(0, str(ROOT / "Assets/Scripts/AI/Evaluation"))
        from headless_report import summarize
        summary = summarize(evaluation, labels, args.run_id)
        selected = summary["selected_for_inspection"]
        publish = ROOT / "Assets/Models/AI" / args.run_id
        publish.mkdir(parents=True, exist_ok=True)
        published = []
        for label in sorted({selected, f"{args.run_id}_{final}"}):
            target = publish / (label + ".onnx")
            if target.exists() and digest(target) != digest(models / target.name):
                raise ValueError("Existing inspection model differs; refusing overwrite")
            if not target.exists():
                shutil.copy2(models / target.name, target)
            published.append(str(target))
        instructions = (f"# Model {args.run_id} untuk ditonton\n\nTerpilih dari checkpoint yang diuji: {selected}.\n"
                        f"Model akhir: {args.run_id}_{final}.\n\n"
                        "Buka AI_Phase2_SmallWFC, Stop trainer, pasang model ONNX pada Behavior Parameters, "
                        "pilih Inference Only, lalu Play. Jangan Heuristic Only. "
                        "Default lesson scene dapat berbeda dari protokol uji; hasil visual tidak menggantikan evaluasi berpasangan.\n\n"
                        f"Laporan: {evaluation / 'HASIL_EVALUASI.md'}\n"
                        "Model terpilih belum berarti lulus Phase 3; periksa hasil naik/turun dan coverage per lantai.\n")
        (publish / "CARA_LIHAT_MODEL.md").write_text(instructions, encoding="utf-8")
        for path, original in protected.items():
            if digest(path) != original:
                raise RuntimeError(f"Protected main artifact changed externally during the job: {path}")
        update("complete", selected_for_inspection=selected, published_models=published,
               report=str(evaluation / "HASIL_EVALUASI.md"))
    except BaseException as error:
        update("failed", error=f"{type(error).__name__}: {error}")
        raise
    finally:
        for process in processes:
            if process.poll() is None:
                # Only child handles created by this job, never the user's Editor.
                process.terminate()
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    process.kill()
        for log in logs:
            log.close()
        os.close(lock)
        lock_path.unlink(missing_ok=True)


if __name__ == "__main__":
    main()

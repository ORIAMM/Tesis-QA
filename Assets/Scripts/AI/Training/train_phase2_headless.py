"""Same mastery trainer, with a process-local explicit Unity Editor port."""
import argparse
import sys


def main():
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--editor-port", type=int, default=5017)
    custom, remaining = parser.parse_known_args()
    if not 1024 <= custom.editor_port <= 65535:
        raise ValueError("Invalid headless Editor port")
    from mlagents.trainers import learn
    original_factory = learn.create_environment_factory

    def isolated_factory(*args, **kwargs):
        # ML-Agents 1.1 learn.py clears --base-port for an Editor connection.
        # Change only this factory in this Python process, not site-packages.
        if len(args) > 6:
            args = list(args)
            args[6] = custom.editor_port
        else:
            kwargs["start_port"] = custom.editor_port
        return original_factory(*args, **kwargs)

    sys.argv = [sys.argv[0], *remaining]
    learn.create_environment_factory = isolated_factory
    import train_phase2
    train_phase2.main()


if __name__ == "__main__":
    main()

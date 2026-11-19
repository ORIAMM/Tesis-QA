"""Exact episode-success curriculum. Independent of PPO reward magnitude/step count."""
from collections import deque


class MasteryWindow:
    REVISION = 1
    SIZE = 100

    def __init__(self, stage=0, samples=()):
        self.stage = stage
        self.samples = deque(maxlen=self.SIZE)
        for sample in samples:
            self.add(sample)

    def add(self, encoded):
        code = int(encoded)
        stage, outcome = divmod(code, 10)
        if float(encoded) != code or stage not in (0, 1, 2, 3, 4, 5) or outcome not in (0, 1, 2, 3):
            raise ValueError(f"Invalid mastery episode outcome: {encoded}")
        if stage == self.stage:
            self.samples.append(code)

    def summary(self):
        outcomes = [code % 10 for code in tuple(self.samples)]
        up = [x % 2 for x in outcomes if x >= 2]
        down = [x % 2 for x in outcomes if x < 2]
        return {
            "episodes": len(outcomes),
            "success": sum(x % 2 for x in outcomes) / len(outcomes) if outcomes else 0,
            "up_trials": len(up), "down_trials": len(down),
            "up_success": sum(up) / len(up) if up else 0,
            "down_success": sum(down) / len(down) if down else 0,
        }

    def ready(self):
        s = self.summary()
        if s["episodes"] < self.SIZE or self.stage in (3, 5):
            return False
        if self.stage in (0, 4):
            return s["success"] >= 0.75
        if self.stage == 1:
            return s["up_trials"] >= 75 and s["up_success"] >= 0.80
        return (s["up_trials"] >= 20 and s["down_trials"] >= 20
                and s["up_success"] >= 0.75 and s["down_success"] >= 0.75)

    def state(self):
        return {"revision": self.REVISION, "stage": self.stage, "samples": list(self.samples)}

    @classmethod
    def restore(cls, state, stage):
        if not state or state.get("revision") != cls.REVISION or state.get("stage") != stage:
            raise ValueError("Mastery state does not match this curriculum. Restart Phase 2; do not resume the old step-based run.")
        return cls(stage, state["samples"])


class StairPracticeWindow(MasteryWindow):
    """Local lesson-1 levels: full guide -> faded guide -> no guide."""
    def __init__(self, stage=0, samples=()):
        if stage not in (0, 1, 2):
            raise ValueError("Invalid stair practice level")
        super().__init__(stage, samples)

    def ready(self):
        s = self.summary()
        return s["episodes"] >= self.SIZE and s["up_trials"] >= 75 and s["up_success"] >= 0.80


class NearSearchWindow(MasteryWindow):
    """Independent local difficulty levels inside main lesson 2."""
    def __init__(self, stage=0, samples=()):
        if stage not in (0, 1, 2, 3):
            raise ValueError("Invalid near-search level")
        super().__init__(stage, samples)

    def ready(self):
        s = self.summary()
        return (s["episodes"] >= self.SIZE and s["up_trials"] >= 20 and s["down_trials"] >= 20
                and s["up_success"] >= 0.75 and s["down_success"] >= 0.75)


class MixedSearchWindow:
    """Level*100 + far*4 + ascent*2 + success; exact per-episode, not summed batches."""
    REVISION = 1
    SIZE = 100
    LAST_LEVEL = 4

    def __init__(self, stage=0, samples=()):
        if stage not in range(self.LAST_LEVEL + 1):
            raise ValueError("Invalid mixed-search level")
        self.stage = stage
        self.samples = deque(maxlen=self.SIZE)
        for sample in samples:
            self.add(sample)

    def add(self, encoded):
        code = int(encoded)
        level, outcome = divmod(code, 100)
        if float(encoded) != code or level not in range(self.LAST_LEVEL + 1) or outcome not in range(8):
            raise ValueError(f"Invalid mixed-search outcome: {encoded}")
        if level == self.stage:
            self.samples.append(code)

    def summary(self):
        outcomes = [code % 100 for code in self.samples]
        def select(mask, value):
            return [x % 2 for x in outcomes if x & mask == value]
        up, down = select(2, 2), select(2, 0)
        near, far = select(4, 0), select(4, 4)
        def rate(values):
            return sum(values) / len(values) if values else 0.0
        return {"episodes": len(outcomes), "success": rate([x % 2 for x in outcomes]),
                "up_trials": len(up), "down_trials": len(down),
                "up_success": rate(up), "down_success": rate(down),
                "near_trials": len(near), "far_trials": len(far),
                "near_success": rate(near), "far_success": rate(far)}

    def ready(self):
        s = self.summary()
        return (s["episodes"] >= self.SIZE and min(s["up_trials"], s["down_trials"]) >= 20
                and min(s["up_success"], s["down_success"]) >= 0.75
                and s["near_trials"] >= 20 and s["near_success"] >= 0.75
                and (self.stage == 0 or s["far_trials"] >= 10 and s["far_success"] >= 0.75))

    def state(self):
        return {"revision": self.REVISION, "stage": self.stage, "samples": list(self.samples)}

    @classmethod
    def restore(cls, state, stage):
        if not state or state.get("revision") != cls.REVISION or state.get("stage") != stage:
            raise ValueError("Mixed-search state missing/mismatched; do not silently reset a resumed run.")
        return cls(stage, state["samples"])

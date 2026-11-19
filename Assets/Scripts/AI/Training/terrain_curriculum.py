"""Phase2T mastery state; independent from legacy Phase2M* curricula."""
from collections import deque

LAST_LEVEL = 11
NAMES = ["Single20", "Single30", "Single40", "TransferMouth", "Transfer1",
         "Transfer2", "Transfer3", "Transfer4", "Balanced10", "Balanced20",
         "Balanced30", "Balanced40"]


class TerrainWindow:
    REVISION = 1
    SIZE = 100

    def __init__(self, level=0, samples=()):
        if level not in range(LAST_LEVEL + 1):
            raise ValueError("Invalid terrain curriculum level")
        self.level = level
        self.samples = deque(maxlen=self.SIZE)
        for value in samples:
            self.add(value)

    def add(self, encoded):
        code = int(encoded)
        level, outcome = divmod(code, 100)
        if float(encoded) != code or level not in range(LAST_LEVEL + 1) or outcome >= 32:
            raise ValueError(f"Invalid terrain episode packet: {encoded}")
        # Rehearsal and delayed packets from the preceding difficulty never
        # provide evidence that the CURRENT difficulty has been mastered.
        if level == self.level and not outcome & 16:
            self.samples.append(code)

    def summary(self):
        outcomes = [code % 100 for code in self.samples]
        def group(mask, flag):
            return [x & 1 for x in outcomes if x & mask == flag]
        def rate(values):
            return sum(values) / len(values) if values else 0.0
        up, down = group(2, 2), group(2, 0)
        lower, upper = group(4, 0), group(4, 4)
        near, far = group(8, 0), group(8, 8)
        return {"episodes": len(outcomes), "success": rate([x & 1 for x in outcomes]),
                "up_trials": len(up), "down_trials": len(down),
                "up_success": rate(up), "down_success": rate(down),
                "lower_trials": len(lower), "upper_trials": len(upper),
                "lower_success": rate(lower), "upper_success": rate(upper),
                "near_trials": len(near), "far_trials": len(far),
                "near_success": rate(near), "far_success": rate(far)}

    def ready(self):
        s = self.summary()
        if s["episodes"] < self.SIZE or s["success"] < 0.8:
            return False
        if 3 <= self.level <= 7:
            if min(s["up_trials"], s["down_trials"]) < 20 or min(s["up_success"], s["down_success"]) < 0.8:
                return False
            if self.level >= 6:
                return (s["far_trials"] >= 10 and s["far_success"] >= 0.8
                        and s["near_trials"] >= 20 and s["near_success"] >= 0.8)
        if self.level >= 8:
            return (min(s["lower_trials"], s["upper_trials"]) >= 20
                    and min(s["lower_success"], s["upper_success"]) >= 0.8)
        return True

    def state(self):
        return {"revision": self.REVISION, "level": self.level, "samples": list(self.samples)}

    @classmethod
    def restore(cls, saved, level):
        if not saved or saved.get("revision") != cls.REVISION or saved.get("level") != level:
            raise ValueError("Terrain mastery state missing/mismatched; do not resume a legacy run.")
        return cls(level, saved["samples"])

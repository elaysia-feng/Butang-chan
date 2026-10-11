"""只读取制作前后的帧，检查站立动作的落脚点和下半身闪烁。"""
import json
from pathlib import Path

import numpy as np
from PIL import Image

# 1. 两套动画按相同320×400像素尺度比较，避免把分辨率差异算成抖动。
root = Path(__file__).resolve().parents[2]
old = json.loads((root / "legacy/motions-hard-cuts.json").read_text(encoding="utf-8"))
new = json.loads((root / "assets/motions/manifest.json").read_text(encoding="utf-8"))
report = {}
for name in ["idle", "wave", "talk", "shy", "sleepy"]:
    values = {}
    for label, manifest in [("before", old), ("after", new)]:
        frames = [np.array(Image.open(root / "assets/motions" / file).convert("RGBA").resize((320, 400), Image.Resampling.BICUBIC))
                  for file in manifest["clips"][name]["frames"]]
        positions = []
        differences = []
        previous = frames[0][242:400].astype(np.int16)
        for rgba in frames:
            y, x = np.where(rgba[350:400, :, 3] >= 128)
            positions.append(float(np.mean(x)))
            lower = rgba[242:400].astype(np.int16)
            differences.append(float(np.mean(np.abs(lower - previous))))
            previous = lower
        values[label] = {"frames": len(frames), "foot_x_range": round(max(positions) - min(positions), 4),
                         "lower_body_change": round(max(differences), 4)}
    # 2. 站立时脚和衣服不应随着另一只手的动作一起闪动。
    if values["after"]["foot_x_range"] > 0.1 or values["after"]["lower_body_change"] > 0.1:
        raise RuntimeError(f"{name}的下半身仍有抖动：{values['after']}")
    report[name] = values
# 3. 留下可复查的数据，视觉检查仍需观看完整播放。
output = root / "app/native-qa/stability.json"
output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False))

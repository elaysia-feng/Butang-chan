"""为已对齐的角色序列生成透明中间帧；模型只在制作阶段使用，不进入桌宠运行依赖。"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image
import torch
from torch.nn import functional as F


def load_frame(path: Path) -> tuple[torch.Tensor, np.ndarray]:
    """预乘Alpha后送入模型，防止透明像素的隐藏颜色产生边缘光晕。

    Args:
        path: 已对齐的320×400透明关键帧。

    Returns:
        填充后的预乘RGBA张量，以及用于固定纹理的原始RGBA数组。
    """
    rgba = np.asarray(Image.open(path).convert("RGBA"), dtype=np.float32) / 255.0
    premultiplied = rgba.copy()
    premultiplied[:, :, :3] *= premultiplied[:, :, 3:4]
    tensor = torch.from_numpy(premultiplied.transpose(2, 0, 1).copy()).unsqueeze(0)
    return F.pad(tensor, (0, 64, 0, 112)), rgba


def save_frame(tensor: torch.Tensor, path: Path, neutral: np.ndarray, fixed_body: bool) -> None:
    """还原直通Alpha，并固定站立动作的裙摆和腿。

    Args:
        tensor: 模型输出的预乘RGBA中间帧。
        path: 输出透明PNG路径。
        neutral: 站立动作共同使用的中性纹理。
        fixed_body: 是否锁定裙摆和腿；走路和跳跃不锁定。
    """
    # 1. 还原预乘颜色，透明区域保持零色，保留发丝与蕾丝的半透明边缘。
    rgba = tensor[0, :, :400, :320].clamp(0, 1).numpy().transpose(1, 2, 0).copy()
    alpha = rgba[:, :, 3:4]
    rgba[:, :, :3] = np.clip(rgba[:, :, :3] / np.maximum(alpha, 1e-5), 0, 1)
    rgba[rgba[:, :, 3] < 0.003] = 0
    # 2. 补帧也不能移动静止的落脚点，站立时用同一套下半身纹理。
    if fixed_body:
        blend = np.clip((np.arange(400, dtype=np.float32) - 220) / 22, 0, 1)[:, None, None]
        rgba = rgba * (1 - blend) + neutral * blend
    Image.fromarray(np.round(rgba * 255).astype(np.uint8), "RGBA").save(path)


def load_model() -> torch.nn.Module:
    """加载仅供离线制作使用的轻量补帧模型。

    Returns:
        已加载权重、关闭梯度并使用CPU的推理网络。
    """
    model_root = Path(__file__).parent / "rife" / "model"
    sys.path.insert(0, str(model_root.resolve()))
    from train_log.IFNet_HDv3 import IFNet
    torch.set_num_threads(4)
    torch.set_grad_enabled(False)
    model = IFNet().eval()
    weights = torch.load(model_root / "train_log" / "flownet.pkl", map_location="cpu", weights_only=True)
    required = set(model.state_dict())
    model.load_state_dict({key.removeprefix("module."): value for key, value in weights.items()
                           if key.removeprefix("module.") in required}, strict=True)
    return model


def run(name: str, prepared: Path, output: Path, limit: int | None) -> None:
    """按动作时间线计算中间帧，输出均匀30fps的透明PNG和帧清单。

    Args:
        name: 动作名，或走路进出场及左右朝向的过渡名。
        prepared: 已校正关键帧与共同中性姿态所在目录。
        output: 制作结果目录，不覆盖原始生成图集。
        limit: 调试时限制每段帧数；正式制作为None。
    """
    # 1. 加载官方轻量模型与本机已对齐的关键帧，使用CPU而不改动全局Python环境。
    model = load_model()
    frames = [load_frame(prepared / f"rgba-{index:02d}.png")[0] for index in range(8)]
    neutral_tensor, neutral = load_frame(prepared / "neutral-rgba.png")
    base_name = name.split("-")[0] if name.startswith(("walk-", "lift-", "climb-")) else name
    durations = {
        "idle": [.65, .5, .4, .09, .09, .1, .5, .5],
        "wave": [.18, .2, .18, .16, .16, .16, .22, .26],
        "walk": [.17] * 8,
        "cheer": [.3, .3, .32, .24, .3, .3, .32, .35],
        "talk": [.18] * 8,
        "shy": [.25] * 8,
        "sleepy": [.3, .3, .28, .3, .4, .3, .3, .35],
        "lift": [.24] * 8,
        "climb": [.2] * 8,
    }[base_name]
    moving = base_name in {"walk", "cheer", "lift", "climb"}
    if name == "walk":
        frames[7] = frames[0]
    keys = list(frames)
    if name == "lift":
        keys = [frames[3], frames[5], frames[7], frames[3]]
        durations = [.4] * 3
    elif name == "climb":
        keys = frames[:4] + [frames[0]]
        durations = [.24] * 4
    elif name.startswith(("lift-", "climb-")):
        pose = frames[3] if base_name == "lift" else frames[0]
        if name.endswith("-left"):
            pose = torch.zeros_like(pose)
            pose[:, :, :400, :320] = torch.flip((frames[3] if base_name == "lift" else frames[0])[:, :, :400, :320], dims=[3])
        keys = [neutral_tensor, pose] if "entry" in name else [pose, neutral_tensor]
        durations = [.3]
    elif name.startswith("walk-"):
        turn = frames[0]
        if name.endswith("-left"):
            turn = torch.zeros_like(turn)
            turn[:, :, :400, :320] = torch.flip(frames[0][:, :, :400, :320], dims=[3])
        keys = [neutral_tensor, turn] if "entry" in name else [turn, neutral_tensor]
        durations = [.32]
    elif name in {"idle", "walk"}:
        keys.append(frames[0])
    else:
        keys = [neutral_tensor] + keys + [neutral_tensor]
        durations = [.24] + durations
    output.mkdir(parents=True, exist_ok=True)
    # 2. 模型同时变换颜色和Alpha，不用色键，不把两张姿态透明叠在一起。
    count = 0
    for pair in range(len(keys) - 1):
        steps = max(2, round(durations[pair] * 30))
        if limit is not None:
            steps = min(steps, limit)
        first, second = keys[pair], keys[pair + 1]
        same = torch.equal(first, second)
        for step in range(steps):
            t = step / steps
            if step == 0 or same:
                result = first
            else:
                result = model(torch.cat((first, second), 1), timestep=t, scale_list=[32, 16, 8, 4, 1])[-1][-1]
            save_frame(result, output / f"frame-{count:03d}.png", neutral, not moving)
            count += 1
        print(f"{name}: 完成 {pair + 1}/{len(keys) - 1} 段，{count}帧", flush=True)
    # 3. 非循环动作以共同的中性姿态收尾，切回待机时不跳脸、不跳大小。
    if name not in {"idle", "walk", "lift", "climb"}:
        save_frame(keys[-1], output / f"frame-{count:03d}.png", neutral, not moving)
        count += 1
    manifest = {"frames": [f"frame-{index:03d}.png" for index in range(count)], "durations": [33] * count,
                "loop": name in {"idle", "walk", "talk", "lift", "climb"}, "size": [320, 400], "model": "RIFE 4.25 lite"}
    (output / "clip.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("name")
    parser.add_argument("prepared", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--limit", type=int)
    arguments = parser.parse_args()
    run(arguments.name, arguments.prepared, arguments.output, arguments.limit)

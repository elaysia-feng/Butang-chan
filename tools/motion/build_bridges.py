"""从实际播放帧制作短衔接，交互最多等待四帧，不等待完整手势结束。"""
from pathlib import Path
import json
import torch

from interpolate_clip import load_frame, load_model, save_frame


def run(root: Path) -> None:
    """生成提起和回中性姿态的光流衔接，并原子更新播放清单。

    Args:
        root: 包含 manifest.json 和透明播放帧的动作资源目录。
    """
    # 1. 只处理原动作，重复制作不能把已有衔接再次展开。
    manifest_path = root / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    clips = {name: spec for name, spec in manifest["clips"].items() if not name.startswith("bridge-")}
    model = load_model()
    goals = {"lift": load_frame(root / clips["lift"]["frames"][0])[0],
             "settle": load_frame(root / clips["idle"]["frames"][0])[0]}
    _, neutral = load_frame(root / clips["idle"]["frames"][0])
    bridges = {}
    for name, clip in clips.items():
        if name == "talk":
            continue
        orientations = [False, True] if name in {"walk", "climb"} else [False]
        for left in orientations:
            source_name = name + ("-left" if left else "")
            for index in range(0, len(clip["frames"]), 4):
                first, _ = load_frame(root / clip["frames"][index])
                if left:
                    mirrored = torch.zeros_like(first)
                    mirrored[:, :, :400, :320] = torch.flip(first[:, :, :400, :320], dims=[3])
                    first = mirrored
                for goal, last in goals.items():
                    bridge = f"bridge-{goal}-{source_name}-{index:03d}"
                    output = root / "bridges" / bridge
                    output.mkdir(parents=True, exist_ok=True)
                    files = []
                    # 2. 首帧等于取样时的真实姿态，末帧等于目标，避免透明叠影或直接换图。
                    for step in range(5):
                        result = first if step == 0 else last if step == 4 else model(
                            torch.cat((first, last), 1), timestep=step / 4,
                            scale_list=[32, 16, 8, 4, 1])[-1][-1]
                        path = output / f"frame-{step:02d}.png"
                        save_frame(result, path, neutral, False)
                        files.append(path.relative_to(root).as_posix())
                    bridges[bridge] = {"frames": files, "durations": [33] * 5, "loop": False}
            print(f"完成 {source_name} 的短衔接", flush=True)
    # 3. 资源齐全后再发布清单，运行时不需要模型或 Python。
    manifest["clips"] = clips | bridges
    manifest["bridgeSampleStep"] = 4
    temporary = manifest_path.with_suffix(".tmp")
    temporary.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(manifest_path)
    print(f"完成 {len(bridges)} 组衔接", flush=True)


if __name__ == "__main__":
    run(Path(__file__).resolve().parents[2] / "assets" / "motions")

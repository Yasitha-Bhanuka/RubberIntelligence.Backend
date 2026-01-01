"""
========================================================
Pests Recognition - FastAI → ONNX (.NET Ready)
========================================================

OUTPUTS:
- pests_model.onnx        (for .NET ONNX Runtime)
- pests_labels.json       (index → class mapping)
- pests_fastai.pkl        (FastAI model backup)

.NET BACKEND EXPECTATIONS:
- Input: float32 tensor [N,3,224,224]
- RGB, ImageNet normalization
- Output: raw logits (apply Softmax in C#)

========================================================
"""

# ------------------------------------------------------
# 1. Imports
# ------------------------------------------------------
import torch
from fastai.vision.all import *
from pathlib import Path
import json

# ------------------------------------------------------
# 2. Environment Check
# ------------------------------------------------------
print("Torch:", torch.__version__)
print("CUDA available:", torch.cuda.is_available())

if torch.cuda.is_available():
    print("GPU:", torch.cuda.get_device_name(0))

# ------------------------------------------------------
# 3. Dataset Path (Pests)
# ------------------------------------------------------
ROOT_DIR = Path("Pests")

if not ROOT_DIR.exists():
    raise FileNotFoundError(f"Dataset not found: {ROOT_DIR}")

# ------------------------------------------------------
# 4. DataBlock (ImageNet compatible)
# ------------------------------------------------------
"""
FastAI automatically applies ImageNet normalization
internally. This MUST be replicated in .NET backend.
"""

disease_block = DataBlock(
    blocks=(ImageBlock, CategoryBlock),
    get_items=get_image_files,
    splitter=RandomSplitter(valid_pct=0.2, seed=42),
    get_y=parent_label,
    item_tfms=Resize(460),
    batch_tfms=[
        *aug_transforms(size=224, min_scale=0.75),
        Normalize.from_stats(*imagenet_stats)
    ]
)

# ------------------------------------------------------
# 5. DataLoaders
# ------------------------------------------------------
def get_dls():
    return disease_block.dataloaders(
        ROOT_DIR,
        bs=32,
        num_workers=0  # set to 0 to avoid windows multiprocessing issues
    )

if __name__ == "__main__":
    dls = get_dls()

    print("Classes:")
    for i, c in enumerate(dls.vocab):
        print(f"{i}: {c}")

    # ------------------------------------------------------
    # 6. Model
    # ------------------------------------------------------
    learn = vision_learner(
        dls,
        resnet18,
        metrics=accuracy
    )

    # ------------------------------------------------------
    # 7. Training
    # ------------------------------------------------------
    learn.fine_tune(6)

    # ------------------------------------------------------
    # 8. Save FastAI Model (Saved BEFORE ONNX as backup)
    # ------------------------------------------------------
    learn.export("pests_fastai.pkl")
    print("Saved FastAI model: pests_fastai.pkl")

    # ------------------------------------------------------
    # 9. Prepare for ONNX Export
    # ------------------------------------------------------
    learn.model.eval()

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    learn.model.to(device)

    dummy_input = torch.randn(
        1, 3, 224, 224,
        dtype=torch.float32,
        device=device
    )

    # ------------------------------------------------------
    # 10. Export ONNX (NO Softmax)
    # ------------------------------------------------------
    onnx_path = "pests_model.onnx"

    torch.onnx.export(
        learn.model,
        dummy_input,
        onnx_path,
        export_params=True,
        do_constant_folding=True,
        input_names=["input"],
        output_names=["logits"],
        dynamic_axes={
            "input": {0: "batch_size"},
            "logits": {0: "batch_size"}
        },
        opset_version=12
    )

    print(f"Exported ONNX model: {onnx_path}")

    # ------------------------------------------------------
    # 11. Save Labels
    # ------------------------------------------------------
    labels = list(dls.vocab)

    with open("pests_labels.json", "w") as f:
        json.dump(labels, f, indent=4)

    print("Saved pests_labels.json")

    print("Training + Export completed successfully ✅")

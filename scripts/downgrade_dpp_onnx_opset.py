"""
Downgrades dpp_classifier_model_large.onnx from Opset 25 → Opset 14.

Microsoft.ML.OnnxRuntime guarantees support for Opsets up to 21.
Opset 14 is chosen for maximum compatibility across all runtime versions.

HOW TO RUN (any machine with Python 3.8+):
    pip install onnx
    python downgrade_dpp_onnx_opset.py

The script reads the model from its standard location relative to this
scripts/ folder and writes the converted file back to the same location.
"""

import os
import onnx
from onnx import version_converter

# ── Paths ──────────────────────────────────────────────────────────────────
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR  = os.path.join(SCRIPT_DIR, "..", "RubberIntelligence.API", "Modules", "Dpp", "Models")
INPUT_PATH = os.path.join(MODEL_DIR, "dpp_classifier_model_large.onnx")
BACKUP_PATH = INPUT_PATH + ".opset25.bak"
TARGET_OPSET = 14

# ── Validate ───────────────────────────────────────────────────────────────
if not os.path.exists(INPUT_PATH):
    print(f"ERROR: Model not found at:\n  {INPUT_PATH}")
    print("Please verify the path and run again.")
    exit(1)

model = onnx.load(INPUT_PATH)
current_opset = max(op.version for op in model.opset_import if op.domain in ("", "ai.onnx"))
print(f"Loaded model — current opset: {current_opset}")

if current_opset <= TARGET_OPSET:
    print(f"Model is already at opset {current_opset} — no conversion needed.")
    exit(0)

# ── Backup original ────────────────────────────────────────────────────────
if not os.path.exists(BACKUP_PATH):
    import shutil
    shutil.copy2(INPUT_PATH, BACKUP_PATH)
    print(f"Backed up original to: {os.path.basename(BACKUP_PATH)}")

# ── Convert ────────────────────────────────────────────────────────────────
print(f"Converting opset {current_opset} → {TARGET_OPSET} ...")
converted = version_converter.convert_version(model, TARGET_OPSET)

# ── Save ───────────────────────────────────────────────────────────────────
onnx.save(converted, INPUT_PATH)
print(f"\n✅ SUCCESS: Saved converted model to:\n  {INPUT_PATH}")
print(f"   New opset: {TARGET_OPSET}")
print("\nRestart 'dotnet run' — the ONNX model will load without errors.")

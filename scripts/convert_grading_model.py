# ---------------------------------------------------------
# TFLITE TO ONNX CONVERTER
# Use this if you ONLY have the .tflite file.
# ---------------------------------------------------------

import os

# 1. Install tf2onnx and downgrade numpy
# tf2onnx is not yet compatible with numpy 2.0
print("Installing libraries (downgrading numpy)...")
os.system("pip install tf2onnx==1.15.1 onnx \"numpy<2.0\"")

# 2. Define Paths
tflite_path = 'RSS_Defect_Model.tflite'
onnx_output_path = 'rubber_grading_model.onnx'

# 3. Check if TFLite file exists
if not os.path.exists(tflite_path):
    print(f"\n❌ ERROR: '{tflite_path}' not found.")
    print("Please upload 'RSS_Defect_Model.tflite' to the Colab 'Files' tab.")
    exit(1)

# 4. Run Conversion Command
print(f"Converting {tflite_path} to ONNX...")
import subprocess

# Run with output capturing to debug errors
result = subprocess.run(
    ["python", "-m", "tf2onnx.convert", "--tflite", tflite_path, "--output", onnx_output_path, "--opset", "13"],
    capture_output=True, text=True
)

# Print the Output logs
print("\n--- STDOUT ---")
print(result.stdout)
print("\n--- STDERR ---")
print(result.stderr)

if result.returncode == 0:
    print(f"\n✅ SUCCESS: Created {onnx_output_path}")
    try:
        from google.colab import files
        files.download(onnx_output_path)
        print("Download initiated.")
    except ImportError:
        print("Running locally - check file system.")
else:
    print(f"\n❌ Conversion Failed with exit code {result.returncode}")

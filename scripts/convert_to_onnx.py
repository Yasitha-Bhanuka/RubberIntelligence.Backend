# ---------------------------------------------------------
# INSTRUCTIONS:
# 1. In Google Colab, go to Menu > Runtime > "Disconnect and Delete Runtime" (or Factory Reset).
#    (This is CRITICAL to clear the broken installation errors).
# 2. Upload your 'rubber_classifier_model.keras' file to Colab files.
# 3. Paste this code into a cell and run it.
# ---------------------------------------------------------

# 1. Install tf2onnx (This works on a clean runtime)
import os
os.system('pip install -U tf2onnx onnx')

import tensorflow as tf
import tf2onnx
import onnx
from google.colab import files

# 2. Verify File
model_path = 'rubber_classifier_model.keras'
if not os.path.exists(model_path):
    print(f"ERROR: '{model_path}' not found. Please upload it to the Files tab on the left.")
    exit(1)

print(f"Loading {model_path}...")
try:
    # Load Keras model
    model = tf.keras.models.load_model(model_path)
except Exception as e:
    print(f"Error loading model: {e}")
    exit(1)

# 3. Convert
print("Converting to ONNX...")
spec = (tf.TensorSpec((None, 224, 224, 3), tf.float32, name="input_layer"),)
model_proto, _ = tf2onnx.convert.from_keras(model, input_signature=spec, opset=13)

# 4. Save & Download
output_path = "rubber_leaf_disease_model.onnx"
onnx.save(model_proto, output_path)
print(f"SUCCESS: Saved to {output_path}")

files.download(output_path)
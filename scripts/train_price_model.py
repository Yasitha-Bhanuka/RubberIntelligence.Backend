# ---------------------------------------------------------
# AUTO-INSTALL DEPENDENCIES
# This ensures the script works immediately in Google Colab
# ---------------------------------------------------------
import os
print("Installing dependencies...")
os.system("pip install skl2onnx onnx pandas scikit-learn")

import pandas as pd
import numpy as np
from sklearn.model_selection import train_test_split
from sklearn.pipeline import Pipeline
from sklearn.compose import ColumnTransformer
from sklearn.preprocessing import StandardScaler, OneHotEncoder
from sklearn.impute import SimpleImputer
from sklearn.ensemble import RandomForestRegressor
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType, StringTensorType
import onnx

# 1. Load Data
csv_path = 'rubber_auction_dataset_RDD.csv'
if not os.path.exists(csv_path):
    print(f"ERROR: '{csv_path}' not found. Please upload it.")
    # Fallback for testing/demo purposes if file is missing (Optional)
    exit(1)

print(f"Loading {csv_path}...")
df = pd.read_csv(csv_path)

# 2. Define Features & Target
# 2. Define Features & Target
TARGET = 'auction_price_lkr_per_kg'
# UPDATED: Removed numeric moisture/dirt, added categorical versions
NUMERIC_FEATURES = ['quantity_kg', 'visual_quality_score']
CATEGORICAL_FEATURES = ['rubber_sheet_grade', 'district', 'Moisture_Level', 'Cleanliness']

# DATA TRANSFORMATION: Create categorical columns if they don't exist
# This allows training on the existing dataset which only has numeric pct
if 'Moisture_Level' not in df.columns:
    print("Creating synthetic 'Moisture_Level' from 'moisture_content_pct'...")
    conditions = [
        (df['moisture_content_pct'] < 1.0),
        (df['moisture_content_pct'] >= 1.0) & (df['moisture_content_pct'] <= 3.0),
        (df['moisture_content_pct'] > 3.0)
    ]
    choices = ['Dry', 'Normal', 'Wet']
    # Use 'Normal' as default if NaN or outside range, though dataset should be clean
    df['Moisture_Level'] = np.select(conditions, choices, default='Normal')

if 'Cleanliness' not in df.columns:
    print("Creating synthetic 'Cleanliness' from 'dirt_content_pct'...")
    conditions = [
        (df['dirt_content_pct'] < 1.0),
        (df['dirt_content_pct'] >= 1.0) & (df['dirt_content_pct'] <= 3.0),
        (df['dirt_content_pct'] > 3.0)
    ]
    choices = ['Clean', 'Slight', 'Dirty']
    df['Cleanliness'] = np.select(conditions, choices, default='Slight')

print("Features configured.")
print(f"Numeric: {NUMERIC_FEATURES}")
print(f"Categorical: {CATEGORICAL_FEATURES}")

X = df[NUMERIC_FEATURES + CATEGORICAL_FEATURES]
y = df[TARGET]

# FIX: Fill missing categorical values in Pandas to avoid ONNX SimpleImputer error
X[CATEGORICAL_FEATURES] = X[CATEGORICAL_FEATURES].fillna("Unknown")

# 3. Define Preprocessing Pipeline
# We use a ColumnTransformer to handle Mixed Types
# Note: For ONNX, we must ensure types are compatible.
numeric_transformer = Pipeline(steps=[
    ('imputer', SimpleImputer(strategy='median')),
    ('scaler', StandardScaler())
])

categorical_transformer = Pipeline(steps=[
    # Imputer removed here, handled in Pandas above
    ('onehot', OneHotEncoder(handle_unknown='ignore', sparse_output=False))
])

preprocessor = ColumnTransformer(
    transformers=[
        ('num', numeric_transformer, NUMERIC_FEATURES),
        ('cat', categorical_transformer, CATEGORICAL_FEATURES)
    ],
    verbose_feature_names_out=False
)

# 4. Create Full Pipeline
# Random Forest Regressor
model = RandomForestRegressor(n_estimators=100, random_state=42)

pipeline = Pipeline(steps=[
    ('preprocessor', preprocessor),
    ('model', model)
])

# 5. Train Model
print("Training Model...")
X_train, X_test, y_train, y_test = train_test_split(X, y, test_size=0.2, random_state=42)
pipeline.fit(X_train, y_train)

print(f"Training Score (R2): {pipeline.score(X_train, y_train):.4f}")
print(f"Test Score (R2): {pipeline.score(X_test, y_test):.4f}")

# 6. Convert to ONNX
print("Converting to ONNX...")

# Define initial types for ONNX
# Inputs will be passed as named inputs corresponding to feature names
initial_types = []
for name in NUMERIC_FEATURES:
    initial_types.append((name, FloatTensorType([None, 1])))
for name in CATEGORICAL_FEATURES:
    initial_types.append((name, StringTensorType([None, 1])))

# Convert
onnx_model = convert_sklearn(
    pipeline,
    initial_types=initial_types,
    target_opset=12,
    doc_string="Rubber Price Predictor"
)

# 7. Save and Download
output_path = "rubber_price_model.onnx"
with open(output_path, "wb") as f:
    f.write(onnx_model.SerializeToString())

print(f"SUCCESS: Saved to {output_path}")

try:
    from google.colab import files
    files.download(output_path)
    print("Download initiated.")
except ImportError:
    print("Running locally - check file system for output.")

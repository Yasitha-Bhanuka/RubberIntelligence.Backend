import torch
import onnxruntime as ort
import numpy as np
import matplotlib.pyplot as plt
import seaborn as sns
from fastai.vision.all import *
from sklearn.metrics import confusion_matrix, classification_report, accuracy_score
from pathlib import Path

def plot_confusion_matrix(true_labels, pred_labels, vocab, title="Confusion Matrix", filename="confusion_matrix.png"):
    """Plots a confusion matrix using Seaborn and saves it to a file."""
    cm = confusion_matrix(true_labels, pred_labels)
    plt.figure(figsize=(10, 8))
    sns.heatmap(cm, annot=True, fmt='d', cmap='Blues', xticklabels=vocab, yticklabels=vocab)
    plt.ylabel('Actual')
    plt.xlabel('Predicted')
    plt.title(title)
    print(f"Saving confusion matrix to {filename}")
    plt.savefig(filename)
    plt.close()

def evaluate_onnx_model(model_path, data_path, output_image, seed=42):
    """
    Recreates the validation dataloader, runs ONNX inference, and prints metrics.
    """
    print(f"\n{'='*20}\nEvaluating: {model_path}\nDataset: {data_path}\n{'='*20}")
    
    if not Path(data_path).exists():
        print(f"Error: Dataset path '{data_path}' does not exist.")
        return

    # 1. Recreate DataLoaders (Must match training Logic)
    # Note: FastAI aug_transforms and sizing logic is copied from training scripts
    try:
        dblock = DataBlock(
            blocks=(ImageBlock, CategoryBlock),
            get_items=get_image_files,
            splitter=RandomSplitter(valid_pct=0.2, seed=seed),
            get_y=parent_label,
            item_tfms=Resize(460),
            batch_tfms=[*aug_transforms(size=224, min_scale=0.75), Normalize.from_stats(*imagenet_stats)]
        )
        
        dls = dblock.dataloaders(data_path, bs=32, num_workers=0)
    except Exception as e:
        print(f"Error creating DataLoaders: {e}")
        return

    print(f"Classes: {dls.vocab}")
    
    # 2. Initialize ONNX Session
    try:
        sess = ort.InferenceSession(model_path)
    except Exception as e:
        print(f"Error loading ONNX model '{model_path}': {e}")
        return

    input_name = sess.get_inputs()[0].name
    output_name = sess.get_outputs()[0].name
    
    true_y = []
    pred_y = []
    
    # 3. Validation Loop
    print("Running inference on validation set...")
    try:
        for batch in dls.valid:
            # Get batch - images are already preprocessed/normalized by fastai
            imgs, targets = batch
            
            # Convert to numpy for ONNX
            x_np = imgs.cpu().numpy()
            
            # Run Inference
            outputs = sess.run([output_name], {input_name: x_np})
            logits = outputs[0]
            
            # Argmax to get predictions
            preds = np.argmax(logits, axis=1)
            
            true_y.extend(targets.cpu().numpy())
            pred_y.extend(preds)
    except Exception as e:
        print(f"Error during inference: {e}")
        return
        
    # 4. Results
    if not true_y:
        print("No validation data found or processed.")
        return

    acc = accuracy_score(true_y, pred_y)
    print(f"\nOverall Accuracy: {acc:.2%}")
    
    print("\nClassification Report:")
    print(classification_report(true_y, pred_y, target_names=dls.vocab))
    
    plot_confusion_matrix(true_y, pred_y, dls.vocab, title=f"Confusion Matrix: {model_path}", filename=output_image)

if __name__ == "__main__":
    # Evaluate Rubber Disease Model
    evaluate_onnx_model(model_path="rubber_disease_model.onnx", data_path="Leafs", output_image="rubber_disease_cm.png")

    # Evaluate Pests Model
    evaluate_onnx_model(model_path="pests_model.onnx", data_path="Pests", output_image="pests_cm.png")

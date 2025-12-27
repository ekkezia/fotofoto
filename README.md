# 📸 Fotofoto — Mixed Reality Photo Capture & Remix App

**Fotofoto** is a Unity-based Mixed Reality app that lets users **capture photos directly in MR space** using intuitive **hand gestures**, then **remix, arrange, save, and reload** them as spatial image planes.  
Photos can also be imported by scanning a **QR code**, allowing users to blend new MR snapshots with existing images.

The app is built using the **Meta Interaction SDK**, **NuGet** for asset-loading dependencies, and the **Webcam Passthrough Manager** from @xrdevrob’s open-source QuestCameraKit project.

---

## ✨ Features

### **1. Solo Foto**

Capture a new photo using passthrough.

- Uses the `LiveMaskAndCapture` script
- Depends on `HandGestureDetection` for L-frame cropping + index-flick capture

### **2. Remix Foto**

Remix, reposition, and arrange existing images in MR space.

- Uses the `QRCodeDetection` script
- Also depends on the same gesture system

### **3. Load Foto**

Scan a QR code to import an external image as a floating MR image plane.

### **4. Save Foto**

Persist all placed and captured image planes to reload on next launch.

### **5. Clear**

Remove all photos currently in the session.

---

## 🖐 Gesture-Based Interaction

Fotofoto uses a hands-only interaction model powered by the **HandGestureDetection** script.

### **📐 L-Shape Framing Gesture (Crop Preview)**

The app tracks the **corner between the thumb and index metacarpal**, forming an “L” shape.  
This creates a **live crop frame** in MR space so users can visually frame their shot before capturing.

Used in:

- Solo Foto (`LiveMaskAndCapture`)
- Remix Foto (`QRCodeDetection`)

### **⚡ Index Flick Gesture (Capture Trigger)**

A quick flick of the index finger executes the actual capture.

Triggers:

- Finalizing the crop
- Capturing the passthrough frame
- Spawning a new image plane

These two gestures power the entire Fotofoto UX.

---

## 🔧 Script Architecture

- **HandGestureDetection**  
  Core gesture detection for framing + capture.

- **LiveMaskAndCapture**  
  Uses passthrough + gestures to capture new MR photos.

- **QRCodeDetection**  
  Scans/imports QR codes and also uses gesture framing logic.

---

## 🏗 Project Setup

### **Unity Version**

- Recommended: **Unity 2022 LTS or later**
- Target platform: **Meta Quest MR**

### **Required Dependencies**

#### **1. Meta Interaction SDK**

Install via **Unity Package Manager**:
Used for:

- Hand tracking
- Gesture pose data
- Scene interaction

#### **2. QuestCameraKit / Webcam Passthrough Manager**

Provided by Rob (xrdevrob)  
👉 https://github.com/xrdevrob/QuestCameraKit

Used for:

- Stereo passthrough texture stream
- Webcam camera pipeline
- High-quality MR capture

#### **3. NuGet for Unity** (for 3D asset loading & support libs)

Install:  
https://github.com/GlitchEnzo/NuGetForUnity

Used for:

- Optional 3D asset loading
- Utility libraries packaged via NuGet

---

## 🔄 Scene Loading

Fotofoto automatically loads its scene by **searching for any scene with "fotofoto" in its filename**.

Example logic:

```csharp
var scene = UnityEditor.EditorBuildSettings.scenes
    .FirstOrDefault(s => s.path.ToLower().Contains("fotofoto"));

if (scene != null)
{
    SceneManager.LoadScene(scene.path);
}
```

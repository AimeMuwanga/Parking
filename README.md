# Parking Agent

A Unity ML-Agents project that trains a car to park in a marked bay using PPO. The agent drives in a 3D parking lot, observes it through ray sensors plus a few direct inputs, and controls the car with three continuous actions.

## Project features

- **3D environment** with a marked parking lot, parked cars as obstacles and a bay-line layout.
- **Parallel environments:** 8 copies of the lot (`ParkingArea` prefab) train one shared policy at the same time.
- **Sensors:** front and rear ray perception sensors (obstacle detection) combined with hand-picked vector observations.
- **Continuous, 3-dimensional action space:** steering, throttle (negative = reverse) and brake, each in [-1, 1].
- **Randomized target:** at each episode reset the target bay is chosen at random (see below).
- **Own training configuration** (`parking_config.yaml`) and a **hyperparameter comparison** of network sizes in TensorBoard.

## How the agent works

- **Observations:** the target bay's position and direction relative to the car, current speed and steering, plus the ray sensors. Everything is expressed in the car's own frame, so all lots can share one policy.
- **Actions:** steering, throttle, brake (continuous).
- **Reward:** potential-based shaping (progress toward the bay and alignment with it), a small time penalty, a bonus for parking (near the bay, aligned and almost stopped) and a penalty for hitting an obstacle.
- **Episode ends** on a successful park, a crash, or after the step limit.

## Requirements

- Unity **2022.3.33f1**
- ML-Agents Unity package **2.0.2**
- Python **3.9** with `mlagents==0.30.0` (and `protobuf==3.20.3`)

## Open and train

Open the project with Unity and load `Assets/Scenes/SampleScene.unity`. From the project directory, start the trainer:

```powershell
mlagents-learn parking_config.yaml --run-id parking_next
```

Press **Play** in Unity when the trainer is waiting for the environment. `parking_config_128.yaml` keeps the baseline settings but uses a smaller 128-unit, 2-layer network for architecture comparisons:

```powershell
mlagents-learn parking_config_128.yaml --run-id parking_03
```

Before training, set the prefab's Behavior Type to **Default**. Switch it back to **Inference Only** and assign the exported model when evaluating a trained run.

View the recorded runs with TensorBoard:

```powershell
tensorboard --logdir results
```

The committed `results` data contains TensorBoard event files and run configurations; generated checkpoints are excluded.

## Run the trained agent

Close any trainer and press **Play**. `ParkingArea.prefab` is configured for **Inference Only** and uses `Assets/Parking_04.onnx`, exported from `results/parking_04/Parking.onnx`. `Assets/Parking.onnx` is an older model and is not assigned to the prefab.

## Target bays

At each episode reset the target is chosen uniformly from three empty bays: the original center bay, or a bay 6.4 m to either side. The parked cars remain obstacles. The car always starts at the same position.

## Recorded runs

| Run | Target | Network | Steps | Notes |
|---|---|---|---|---|
| `parking_02` | fixed (center bay) | 256 units, 2 layers | 1,640,000 | Baseline. |
| `parking_03` | fixed (center bay) | 128 units, 2 layers | 460,000 captured (maximum 3,000,000) | Only the hidden-unit count differs from the baseline. |
| `parking_04` | randomized (3 bays) | 256 units, 2 layers | 3,000,000 (completed) | Final checkpoint reported 8.4315 reward at 3,000,029 steps. |

### Architecture comparison (`parking_02` vs `parking_03`)

At the captured point, `parking_03` reached 7.67 reward (7.33 smoothed), versus 7.90 for the longer baseline. The training budgets differ, so compare learning trends rather than treating these as equal-duration final results. See the [cumulative reward curves](results/training-comparison.png).

### Randomized-target run (`parking_04`)

`parking_04` trained on the randomized-target task for the full 3,000,000 steps. Its final checkpoint reported 8.4315 reward at 3,000,029 steps, and TensorBoard recorded scalar data through 3M.

Its reward values are **not directly comparable** with `parking_02` and `parking_03`: those runs used a fixed target, while `parking_04` also contains the harder side-bay episodes.

## Results and limitations

With `parking_04` assigned in **Inference Only** mode, Unity testing recorded successful parks in the left, center, and right bays. This confirms the trained model can park in all three target positions; the test was not a controlled estimate of per-bay success rates.

## Repository contents

- `Assets/Scripts/ParkingAgent.cs`: the agent (observations, actions, rewards).
- `Assets/Editor/ParkingAreaBuilder.cs`: editor tool that builds the lot, cars and sensors.
- `Assets/Prefabs/ParkingArea.prefab`: one lot with the agent car; the scene holds 8 copies.
- `parking_config.yaml`, `parking_config_128.yaml`: PPO training configurations.
- `results/`: TensorBoard event files and run configurations for the recorded runs.
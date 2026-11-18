# Parking Agent

A Unity ML-Agents project that trains a car to park in a marked space using PPO.

## Open and train

Open the project with Unity **2022.3.33f1** and load `Assets/Scenes/SampleScene.unity`. From the project directory, start the trainer:

```powershell
mlagents-learn parking_config.yaml --run-id parking_next
```

Press **Play** in Unity when the trainer is waiting for the environment. `parking_config_128.yaml` keeps the baseline settings but uses a smaller 128-unit, 2-layer network for architecture comparisons:

```powershell
mlagents-learn parking_config_128.yaml --run-id parking_03
```

View the recorded runs with TensorBoard:

```powershell
tensorboard --logdir results
```

The committed `results` comparison data contains TensorBoard event files and run configurations; generated checkpoints are excluded.

At each episode reset, the target is uniformly selected from three empty bays: the original center bay or a bay 6.4 m to either side. The parked cars remain obstacles. The recorded `parking_02` and `parking_03` comparison runs used the original fixed target, so retrain before presenting performance on randomized targets.

## Recorded comparison

- `parking_02`: 256 hidden units, 2 layers, 1,640,000 steps.
- `parking_03`: 128 hidden units, 2 layers, captured at 460,000 steps (configured maximum: 3,000,000).

The only architecture change is hidden-unit count. At the captured point, `parking_03` reached 7.67 reward (7.33 smoothed), versus 7.90 for the longer baseline. The training budgets differ, so compare learning trends rather than treating these as equal-duration final results. See the [cumulative reward curves](results/training-comparison.png).

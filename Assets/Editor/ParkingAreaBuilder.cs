#if UNITY_EDITOR
// Menus: Parking > Build Parking Area, Parking > Upgrade Car Visuals In Prefab
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEditor;
using UnityEngine;

public static class ParkingAreaBuilder
{
    const string ObstacleTag = "Obstacle";
    const string PrefabPath = "Assets/Prefabs/ParkingArea.prefab";
    static readonly Vector3 CarSize = new Vector3(2f, 1f, 4.4f);

    [MenuItem("Parking/Build Parking Area")]
    public static void Build()
    {
        EnsureTag(ObstacleTag);

        var asphalt = Mat("Asphalt", new Color(0.22f, 0.22f, 0.25f));
        var wallMat = Mat("Wall", new Color(0.55f, 0.55f, 0.6f));
        var parkedMat = Mat("ParkedCar", new Color(0.75f, 0.25f, 0.25f));
        var spotMat = Mat("Spot", new Color(0.2f, 0.8f, 0.3f));
        var carMat = Mat("AgentCar", new Color(0.2f, 0.4f, 0.9f));

        var root = new GameObject("ParkingArea");
        Undo.RegisterCreatedObjectUndo(root, "Build Parking Area");
        Transform rt = root.transform;

        // Ground + boundary walls (24 x 24 lot)
        Box("Ground", rt, new Vector3(0, -0.1f, 0), new Vector3(24, 0.2f, 24), asphalt, false, true);
        Box("WallN", rt, new Vector3(0, 1, 12), new Vector3(24, 2, 0.5f), wallMat, true, true);
        Box("WallS", rt, new Vector3(0, 1, -12), new Vector3(24, 2, 0.5f), wallMat, true, true);
        Box("WallE", rt, new Vector3(12, 1, 0), new Vector3(0.5f, 2, 24), wallMat, true, true);
        Box("WallW", rt, new Vector3(-12, 1, 0), new Vector3(0.5f, 2, 24), wallMat, true, true);

        // Target spot (visual only, no collider). Its forward axis (+Z) is the spot direction.
        var spot = Box("TargetSpot", rt, new Vector3(0, 0.01f, 8), new Vector3(3.4f, 0.02f, 5.2f), spotMat, false, false);

        // Two parked cars on either side of the empty spot
        ParkedCar(rt, "ParkedCarL", new Vector3(-3.2f, 0.5f, 8), parkedMat);
        ParkedCar(rt, "ParkedCarR", new Vector3(3.2f, 0.5f, 8), parkedMat);

        // Agent car: root has scale 1 (collider + physics), visuals are children without colliders
        var car = new GameObject("Car");
        car.transform.SetParent(rt, false);
        car.transform.localPosition = new Vector3(0, 0.5f, -4f);
        var col = car.AddComponent<BoxCollider>();
        col.size = CarSize;
        var body = car.AddComponent<Rigidbody>();
        body.mass = 1000f;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        CarVisual(car.transform, carMat);

        // ML-Agents components
        var agent = car.AddComponent<ParkingAgent>();   // also adds BehaviorParameters
        agent.area = rt;
        agent.target = spot.transform;
        agent.MaxStep = 2000;

        var bp = car.GetComponent<BehaviorParameters>();
        bp.BehaviorName = "Parking";                    // must match the name in parking_config.yaml
        bp.BrainParameters.VectorObservationSize = 6;
        bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(3);

        var requester = car.AddComponent<DecisionRequester>();
        requester.DecisionPeriod = 5;

        // Ray sensors: wide front fan + smaller rear fan (needed for reversing)
        AddRays(car, "FrontRays", 6, 90f);
        var rear = new GameObject("RearSensor");
        rear.transform.SetParent(car.transform, false);
        rear.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        AddRays(rear, "RearRays", 3, 60f);

        // Top-down camera so you can watch/record it
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0, 32f, 0);
            cam.transform.rotation = Quaternion.Euler(90f, 0, 0);
        }

        Selection.activeGameObject = root;
        Debug.Log("ParkingArea built. Press Play and drive with the arrow keys (Space = brake).");
    }

    // Re-skins the agent car and the parked cars inside the saved prefab so that every
    // copy in the scene updates. Safe to run more than once.
    [MenuItem("Parking/Upgrade Car Visuals In Prefab")]
    public static void UpgradePrefabVisuals()
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var carMat = Mat("AgentCar", new Color(0.2f, 0.4f, 0.9f));
            var parkedMat = Mat("ParkedCar", new Color(0.75f, 0.25f, 0.25f));

            // Agent car: drop the old box visuals, add the car look
            var car = prefabRoot.transform.Find("Car");
            if (car != null)
            {
                var toRemove = new List<GameObject>();
                foreach (Transform child in car)
                {
                    if (child.name == "Body" || child.name == "Nose" || child.name == "CarVisual")
                        toRemove.Add(child.gameObject);
                }
                foreach (var go in toRemove) Object.DestroyImmediate(go);
                CarVisual(car, carMat);
            }

            // Parked cars: replace the scaled cubes with a scale-1 collider object + car visuals
            foreach (var name in new[] { "ParkedCarL", "ParkedCarR" })
            {
                var old = prefabRoot.transform.Find(name);
                if (old == null) continue;
                Vector3 pos = old.localPosition;
                Object.DestroyImmediate(old.gameObject);
                ParkedCar(prefabRoot.transform, name, pos, parkedMat);
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
        Debug.Log("Prefab car visuals upgraded.");
    }

    static void ParkedCar(Transform parent, string name, Vector3 localPos, Material bodyMat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.tag = ObstacleTag;
        var col = go.AddComponent<BoxCollider>();
        col.size = CarSize;
        CarVisual(go.transform, bodyMat);
    }

    // Builds a simple car out of primitives (no colliders). The parent must have scale 1 and its
    // origin in the middle of a 2 x 1 x 4.4 box. Front is +Z: yellow headlights front, red lights rear.
    static void CarVisual(Transform parent, Material bodyMat)
    {
        var glass = Mat("Glass", new Color(0.10f, 0.15f, 0.22f));
        var tyre = Mat("Tyre", new Color(0.04f, 0.04f, 0.04f));
        var head = Mat("Headlight", new Color(1f, 0.95f, 0.6f));
        var tail = Mat("Taillight", new Color(0.9f, 0.08f, 0.08f));

        var visual = new GameObject("CarVisual").transform;
        visual.SetParent(parent, false);

        Part(visual, "Chassis", PrimitiveType.Cube, new Vector3(0, -0.12f, 0), new Vector3(1.9f, 0.5f, 4.3f), bodyMat);
        Part(visual, "Cabin", PrimitiveType.Cube, new Vector3(0, 0.3f, -0.2f), new Vector3(1.6f, 0.45f, 2.0f), glass);
        Part(visual, "Roof", PrimitiveType.Cube, new Vector3(0, 0.55f, -0.2f), new Vector3(1.5f, 0.06f, 1.7f), bodyMat);

        float wx = 0.95f, wz = 1.4f, wy = -0.15f;
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                var w = Part(visual, "Wheel", PrimitiveType.Cylinder,
                             new Vector3(sx * wx, wy, sz * wz), new Vector3(0.7f, 0.15f, 0.7f), tyre);
                w.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

        foreach (float sx in new[] { -0.6f, 0.6f })
        {
            Part(visual, "Headlight", PrimitiveType.Cube, new Vector3(sx, -0.02f, 2.17f), new Vector3(0.4f, 0.15f, 0.1f), head);
            Part(visual, "Taillight", PrimitiveType.Cube, new Vector3(sx, -0.02f, -2.17f), new Vector3(0.4f, 0.15f, 0.1f), tail);
        }
    }

    static Transform Part(Transform parent, string name, PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go.transform;
    }

    static void AddRays(GameObject host, string sensorName, int raysPerDirection, float maxDegrees)
    {
        var rays = host.AddComponent<RayPerceptionSensorComponent3D>();
        rays.SensorName = sensorName;
        rays.DetectableTags = new List<string> { ObstacleTag };
        rays.RaysPerDirection = raysPerDirection;
        rays.MaxRayDegrees = maxDegrees;
        rays.SphereCastRadius = 0.3f;
        rays.RayLength = 15f;
        rays.StartVerticalOffset = 0f;
        rays.EndVerticalOffset = 0f;
    }

    static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 scale,
                          Material mat, bool obstacle, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (obstacle) go.tag = ObstacleTag;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static Material Mat(string name, Color c)
    {
        const string dir = "Assets/Materials";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Materials");
        string path = dir + "/" + name + ".mat";

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader);
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void EnsureTag(string tag)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tags = tagManager.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
            if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;

        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        tagManager.ApplyModifiedProperties();
    }
}
#endif

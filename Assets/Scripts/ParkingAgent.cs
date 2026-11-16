using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// Simple parking agent.
/// Actions (3 continuous, all in [-1, 1]): steering, throttle (negative = reverse), brake (only values > 0 count).
/// Observations (6 floats): spot position in the car's local frame (x, z), spot direction in the
/// car's local frame (x, z), current speed, current steering. Ray sensors are added separately.
[RequireComponent(typeof(Rigidbody))]
public class ParkingAgent : Agent
{
    [Header("References")]
    public Transform area;      // root of this parking area (lets you duplicate the area for parallel training)
    public Transform target;    // centre of the empty spot; its forward axis is the spot direction

    [Header("Car model")]
    public float maxSpeed = 6f;
    public float maxReverseSpeed = 3f;
    public float acceleration = 6f;
    public float brakeStrength = 14f;
    public float rollingDrag = 1.5f;
    public float maxSteerAngle = 35f;
    public float wheelBase = 2.6f;

    [Header("Start pose (local to the area)")]
    public Vector3 startLocalPos = new Vector3(0f, 0.5f, -4f);
    public float startYaw = 0f;

    [Header("Success")]
    public float positionTolerance = 0.6f;   // metres from the centre of the spot
    public float angleTolerance = 10f;       // degrees (head-in or reversed both count)
    public float stopSpeed = 0.3f;           // must be (almost) stopped

    [Header("Rewards")]
    public float shapingScale = 0.5f;
    public float alignmentWeight = 2f;
    public float successReward = 2f;
    public float crashPenalty = 1f;

    Rigidbody rb;
    float speed;
    float steerInput;
    float prevPotential;
    const float LotScale = 20f;   // used to normalise distances

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        if (area == null) area = transform.parent;
    }

    public override void OnEpisodeBegin()
    {
        speed = 0f;
        steerInput = 0f;

        Vector3 pos = area.TransformPoint(startLocalPos);
        Quaternion rot = area.rotation * Quaternion.Euler(0f, startYaw, 0f);
        transform.SetPositionAndRotation(pos, rot);
        rb.position = pos;
        rb.rotation = rot;
        SetVelocity(Vector3.zero);
        rb.angularVelocity = Vector3.zero;

        prevPotential = Potential();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 toTarget = transform.InverseTransformPoint(target.position);
        sensor.AddObservation(toTarget.x / LotScale);
        sensor.AddObservation(toTarget.z / LotScale);

        Vector3 spotDir = transform.InverseTransformDirection(target.forward);
        sensor.AddObservation(spotDir.x);
        sensor.AddObservation(spotDir.z);

        sensor.AddObservation(speed / maxSpeed);
        sensor.AddObservation(steerInput);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var a = actions.ContinuousActions;
        steerInput = Mathf.Clamp(a[0], -1f, 1f);
        float throttle = Mathf.Clamp(a[1], -1f, 1f);
        float brake = Mathf.Clamp01(a[2]);

        DriveCar(throttle, brake);

        // Potential-based shaping: reward getting closer / better aligned, can't be farmed by wiggling.
        float potential = Potential();
        AddReward(shapingScale * (potential - prevPotential));
        prevPotential = potential;

        // Small time penalty so the agent parks quickly.
        AddReward(MaxStep > 0 ? -1f / MaxStep : -0.0005f);

        if (PlanarDistance() < positionTolerance &&
            AngleError() < angleTolerance &&
            Mathf.Abs(speed) < stopSpeed)
        {
            AddReward(successReward);
            EndEpisode();
        }
    }

    void DriveCar(float throttle, float brake)
    {
        float dt = Time.fixedDeltaTime;

        speed += throttle * acceleration * dt;
        speed = Mathf.MoveTowards(speed, 0f, (brake * brakeStrength + rollingDrag) * dt);
        speed = Mathf.Clamp(speed, -maxReverseSpeed, maxSpeed);

        // Bicycle model: yaw rate = v / wheelBase * tan(steer angle)
        float steerRad = steerInput * maxSteerAngle * Mathf.Deg2Rad;
        float yawDeg = (speed / wheelBase) * Mathf.Tan(steerRad) * Mathf.Rad2Deg * dt;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, yawDeg, 0f));

        Vector3 v = transform.forward * speed;
        v.y = GetVerticalVelocity();
        SetVelocity(v);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Obstacle"))
        {
            AddReward(-crashPenalty);
            EndEpisode();
        }
    }

    float PlanarDistance()
    {
        Vector3 d = target.position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    // 0..90 degrees between the car's axis and the spot's axis (driving in or reversing in are both fine).
    float AngleError()
    {
        float a = Vector3.Angle(transform.forward, target.forward);
        return Mathf.Min(a, 180f - a);
    }

    float Potential()
    {
        float d = PlanarDistance();
        float closeness = Mathf.Clamp01(1f - d / 6f);   // alignment only matters near the spot
        return -d - alignmentWeight * (AngleError() / 90f) * closeness;
    }

    // Unity 6 renamed Rigidbody.velocity to linearVelocity; these helpers work on both.
    void SetVelocity(Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }

    float GetVerticalVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity.y;
#else
        return rb.velocity.y;
#endif
    }

    // Drive it yourself to test the scene: arrows/WASD = steer + throttle, Space = brake.
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var a = actionsOut.ContinuousActions;
        a[0] = Input.GetAxis("Horizontal");
        a[1] = Input.GetAxis("Vertical");
        a[2] = Input.GetKey(KeyCode.Space) ? 1f : 0f;
    }
}

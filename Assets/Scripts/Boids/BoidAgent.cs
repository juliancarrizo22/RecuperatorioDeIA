using System.Collections.Generic;
using UnityEngine;

public enum BoidState
{
    Flocking,
    Evade,
    SeekObject,
    Consume,
    Dead,
    Respawn
}

public class BoidAgent : MonoBehaviour
{
    [Header("FSM State")]
    [SerializeField] private BoidState currentState = BoidState.Flocking;

    [Header("Hunter")]
    [SerializeField] private float hunterPerceptionRadius = 10f;
    [SerializeField] private float evadeWeight = 2f;

    [Header("Interest & Consume")]
    [SerializeField] private float interestPerceptionRadius = 12f;
    [SerializeField] private float arriveSlowingDistance = 3f;
    [SerializeField] private float interestReachDistance = 0.8f;
    [SerializeField] private float consumeInterval = 1f;
    [SerializeField] private int consumeDamage = 1;
    private float consumeTimer;
    private InterestObject targetInterest;

    [Header("Movement")]
    [SerializeField] private float maxSpeed = 3f;
    [SerializeField] private float maxSteering = 3f;

    [Header("Perception")]
    [SerializeField] private float separationRadius = 2f;
    [SerializeField] private float alignmentRadius = 5f;
    [SerializeField] private float cohesionRadius = 5f;
    [SerializeField] private float perceptionInterval = 0.15f;

    [Header("Weights")]
    [SerializeField] private float separationWeight = 1f;
    [SerializeField] private float alignmentWeight = 1f;
    [SerializeField] private float cohesionWeight = 1f;

    private Vector3 velocity;
    private BoidHealth health;

    private Collider[] perceptionBuffer = new Collider[64];
    private List<BoidAgent> nearbyBoids = new List<BoidAgent>();
    private List<InterestObject> nearbyInterests = new List<InterestObject>();
    private HunterAgent nearbyHunter;

    private float perceptionTimer;
    private float perceptionRadius;

    public Vector3 Velocity => velocity;
    public BoidState CurrentState => currentState;

    private void Awake()
    {
        health = GetComponent<BoidHealth>();

        perceptionRadius = Mathf.Max(
            hunterPerceptionRadius,
            interestPerceptionRadius,
            separationRadius,
            alignmentRadius,
            cohesionRadius
        );
    }

    private void Start()
    {
        InitializeMovement();
        ChangeState(BoidState.Flocking);
    }

    public void InitializeMovement()
    {
        Vector3 direction = Random.insideUnitSphere;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f)
            direction = Vector3.forward;

        velocity = direction.normalized * maxSpeed;
        perceptionTimer = perceptionInterval;
    }

    private void Update()
    {
        if (health != null && health.IsDead)
        {
            if (currentState != BoidState.Dead)
                ChangeState(BoidState.Dead);

            velocity = Vector3.zero;
            return;
        }

        perceptionTimer += Time.deltaTime;
        if (perceptionTimer >= perceptionInterval)
        {
            RefreshPerception();
            perceptionTimer = 0f;
        }

        EvaluateTransitions();
        ExecuteCurrentStateBehavior();

        transform.position += velocity * Time.deltaTime;
        transform.position = new Vector3(transform.position.x, 1f, transform.position.z);

        if (velocity.sqrMagnitude > 0.001f)
            transform.forward = velocity.normalized;

        KeepInsideBounds();
    }

    private void ChangeState(BoidState newState)
    {
        if (currentState == newState) return;
        currentState = newState;
    }

    private void EvaluateTransitions()
    {
        if (nearbyHunter != null && Vector3.Distance(transform.position, nearbyHunter.transform.position) <= hunterPerceptionRadius)
        {
            ChangeState(BoidState.Evade);
            return;
        }
        else if (currentState == BoidState.Evade)
        {
            ChangeState(targetInterest != null ? BoidState.SeekObject : BoidState.Flocking);
        }

        if (targetInterest == null)
        {
            targetInterest = FindInterest();
        }

        if (targetInterest != null)
        {
            float distToInterest = Vector3.Distance(transform.position, targetInterest.transform.position);
            if (distToInterest <= interestReachDistance)
            {
                ChangeState(BoidState.Consume);
            }
            else if (currentState != BoidState.Consume)
            {
                ChangeState(BoidState.SeekObject);
            }
        }
        else
        {
            if (currentState == BoidState.SeekObject || currentState == BoidState.Consume)
            {
                ChangeState(BoidState.Flocking);
            }
        }
    }

    private void ExecuteCurrentStateBehavior()
    {
        Vector3 steering = Vector3.zero;

        switch (currentState)
        {
            case BoidState.Flocking:
                steering = GetSeparation() * separationWeight +
                           GetAlignment() * alignmentWeight +
                           GetCohesion() * cohesionWeight;
                break;

            case BoidState.Evade:
                steering = GetEvade(nearbyHunter) * evadeWeight;
                steering += GetSeparation() * (separationWeight * 0.5f);
                break;

            case BoidState.SeekObject:
                if (targetInterest != null)
                {
                    steering = GetArrive(targetInterest.transform);
                }
                else
                {
                    ChangeState(BoidState.Flocking);
                }
                break;

            case BoidState.Consume:
                velocity = Vector3.zero;
                if (targetInterest == null)
                {
                    ChangeState(BoidState.Flocking);
                    break;
                }

                consumeTimer += Time.deltaTime;
                if (consumeTimer >= consumeInterval)
                {
                    consumeTimer = 0f;

                    
                    targetInterest.TakeDamage(consumeDamage);

                    if (targetInterest.CurrentHealth <= 0)
                    {
                        if (InterestManager.Instance != null)
                        {
                            InterestManager.Instance.RemoveInterest(targetInterest);
                        }
                        targetInterest = null;
                        ChangeState(BoidState.Flocking);
                    }
                }
                return;

            case BoidState.Dead:
                velocity = Vector3.zero;
                return;
        }

        steering = Vector3.ClampMagnitude(steering, maxSteering);
        velocity += steering * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, maxSpeed);
    }

    private void RefreshPerception()
    {
        nearbyBoids.Clear();
        nearbyInterests.Clear();
        nearbyHunter = null;

        int count = Physics.OverlapSphereNonAlloc(transform.position, perceptionRadius, perceptionBuffer);
        float closestHunterDistance = Mathf.Infinity;

        for (int i = 0; i < count; i++)
        {
            Collider col = perceptionBuffer[i];
            if (col == null) continue;

            BoidAgent otherBoid = col.GetComponentInParent<BoidAgent>();
            if (otherBoid != null && otherBoid != this)
            {
                if (!nearbyBoids.Contains(otherBoid)) nearbyBoids.Add(otherBoid);
                continue;
            }

            HunterAgent hunter = col.GetComponentInParent<HunterAgent>();
            if (hunter != null)
            {
                float distance = (hunter.transform.position - transform.position).sqrMagnitude;
                if (distance < closestHunterDistance)
                {
                    closestHunterDistance = distance;
                    nearbyHunter = hunter;
                }
                continue;
            }

            InterestObject interest = col.GetComponentInParent<InterestObject>();
            if (interest != null && !nearbyInterests.Contains(interest))
            {
                nearbyInterests.Add(interest);
            }
        }
    }

    private Vector3 GetSeparation()
    {
        Vector3 force = Vector3.zero;
        float radiusSquared = separationRadius * separationRadius;

        foreach (BoidAgent other in nearbyBoids)
        {
            if (other == null) continue;
            Vector3 away = transform.position - other.transform.position;
            float distanceSquared = away.sqrMagnitude;

            if (distanceSquared > 0.001f && distanceSquared <= radiusSquared)
            {
                force += away.normalized / Mathf.Sqrt(distanceSquared);
            }
        }
        return SteerTowards(force);
    }

    private Vector3 GetAlignment()
    {
        Vector3 averageVelocity = Vector3.zero;
        float radiusSquared = alignmentRadius * alignmentRadius;
        int neighborCount = 0;

        foreach (BoidAgent other in nearbyBoids)
        {
            if (other == null) continue;
            float distanceSquared = (other.transform.position - transform.position).sqrMagnitude;

            if (distanceSquared <= radiusSquared)
            {
                averageVelocity += other.Velocity;
                neighborCount++;
            }
        }

        if (neighborCount == 0) return Vector3.zero;
        averageVelocity /= neighborCount;
        return SteerTowards(averageVelocity);
    }

    private Vector3 GetCohesion()
    {
        Vector3 center = Vector3.zero;
        float radiusSquared = cohesionRadius * cohesionRadius;
        int neighborCount = 0;

        foreach (BoidAgent other in nearbyBoids)
        {
            if (other == null) continue;
            float distanceSquared = (other.transform.position - transform.position).sqrMagnitude;

            if (distanceSquared <= radiusSquared)
            {
                center += other.transform.position;
                neighborCount++;
            }
        }

        if (neighborCount == 0) return Vector3.zero;
        center /= neighborCount;
        return SteerTowards(center - transform.position);
    }

    private Vector3 SteerTowards(Vector3 desired)
    {
        if (desired.sqrMagnitude < 0.001f) return Vector3.zero;
        Vector3 desiredVelocity = desired.normalized * maxSpeed;
        return Vector3.ClampMagnitude(desiredVelocity - velocity, maxSteering);
    }

    private Vector3 GetEvade(HunterAgent hunter)
    {
        Vector3 futurePosition = hunter.transform.position + hunter.Velocity * 0.5f;
        Vector3 direction = transform.position - futurePosition;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            direction = Random.insideUnitSphere;

        direction.y = 0f;
        return SteerTowards(direction);
    }

    private InterestObject FindInterest()
    {
        InterestObject closest = null;
        float closestDistance = Mathf.Infinity;

        foreach (InterestObject interest in nearbyInterests)
        {
            if (interest == null) continue;
            float distance = Vector3.Distance(transform.position, interest.transform.position);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = interest;
            }
        }
        return closest;
    }

    private Vector3 GetArrive(Transform target)
    {
        Vector3 direction = target.position - transform.position;
        direction.y = 0f;
        float distance = direction.magnitude;

        if (distance < 0.01f) return -velocity;

        float desiredSpeed = maxSpeed;
        if (distance < arriveSlowingDistance)
            desiredSpeed *= distance / arriveSlowingDistance;

        Vector3 desiredVelocity = direction.normalized * desiredSpeed;
        return Vector3.ClampMagnitude(desiredVelocity - velocity, maxSteering);
    }

    private void KeepInsideBounds()
    {
        if (BoidBounds.Instance == null) return;
        transform.position = BoidBounds.Instance.WrapPosition(transform.position);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, separationRadius);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, alignmentRadius);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, cohesionRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(hunterPerceptionRadius, interestPerceptionRadius));
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum HunterState
{
    Patrol,
    Attack,
    Gather,
    Rest
}

public class HunterAgent : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float speed = 4f;
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private float waypointDistance = 0.8f;

    [Header("Perception")]
    [SerializeField] private float perceptionRadius = 12f;
    [SerializeField] private float perceptionInterval = 0.15f;
    [SerializeField] private float lostTargetTolerance = 1.5f;

    [Header("Combat")]
    [SerializeField] private float tba = 3f;
    [SerializeField] private float rangeAttackRadius = 8f;
    [SerializeField] private float meleeAttackRadius = 2f;
    [SerializeField] private int attackDamage = 1;

    [Header("Energy System")]
    [SerializeField] private float maxEnergy = 100f;
    [SerializeField] private float energyConsumptionRate = 20f;
    [SerializeField] private float energyRechargeRate = 35f;
    private float currentEnergy;

    [Header("Gather and Respawn")]
    [SerializeField] private float gatherDistance = 1.5f;
    [SerializeField] private float gatherTime = 2f;
    [SerializeField] private float respawnDelay = 5f;
    [SerializeField] private float respawnWidth = 20f;
    [SerializeField] private float respawnHeight = 20f;

    [Header("Interest Spawning")]
    [SerializeField] private float interestSpawnInterval = 5f;

    private HunterState currentState;
    private int currentWaypoint;
    private int waypointDirection = 1;
    private float lastAttackTime = -Mathf.Infinity;
    private float gatherTimer;
    private float interestSpawnTimer;
    private float perceptionTimer;

    private BoidAgent target;
    private BoidAgent gatherTarget;
    private HunterVisualFeedback visualFeedback;

    private Collider[] perceptionBuffer = new Collider[64];
    private List<BoidAgent> detectedBoids = new List<BoidAgent>();

    public HunterState CurrentState => currentState;
    public BoidAgent Target => target;
    public float PerceptionRadius => perceptionRadius;
    public Vector3 Velocity { get; private set; }
    public float CurrentEnergy => currentEnergy;

    public BoidAgent CurrentTarget =>
        currentState == HunterState.Gather ? gatherTarget : target;
    public float RangeAttackRadius => rangeAttackRadius;

    private void Awake()
    {
        visualFeedback = GetComponent<HunterVisualFeedback>();
        currentEnergy = maxEnergy;
    }

    private void Start()
    {
        RefreshPerception();
        ChangeState(HunterState.Patrol);
    }

    private void Update()
    {
        perceptionTimer += Time.deltaTime;
        if (perceptionTimer >= perceptionInterval)
        {
            RefreshPerception();
            perceptionTimer = 0f;
        }

        switch (currentState)
        {
            case HunterState.Patrol:
                UpdatePatrol();
                break;
            case HunterState.Attack:
                UpdateAttack();
                break;
            case HunterState.Gather:
                UpdateGather();
                break;
            case HunterState.Rest:
                UpdateRest();
                break;
        }
    }

    private void RefreshPerception()
    {
        detectedBoids.Clear();
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            perceptionRadius,
            perceptionBuffer
        );

        for (int i = 0; i < count; i++)
        {
            Collider col = perceptionBuffer[i];
            if (col == null) continue;

            BoidAgent boid = col.GetComponentInParent<BoidAgent>();
            if (boid != null && !detectedBoids.Contains(boid))
            {
                detectedBoids.Add(boid);
            }
        }
    }

    private void ChangeState(HunterState newState)
    {
        if (currentState == newState)
            return;

        currentState = newState;
        Debug.Log("Hunter State: " + currentState);

        if (visualFeedback != null)
        {
            switch (newState)
            {
                case HunterState.Patrol:
                    visualFeedback.ShowNormal();
                    break;

                case HunterState.Attack:
                    visualFeedback.ShowNormal();
                    break;

                case HunterState.Gather:
                    visualFeedback.ShowGather();
                    break;

                case HunterState.Rest:
                    visualFeedback.ShowRest();
                    break;
            }
        }
    }

    private void UpdatePatrol()
    {
        BoidAgent deadBoid = FindBoid(true);
        if (deadBoid != null)
        {
            gatherTarget = deadBoid;
            ChangeState(HunterState.Gather);
            return;
        }

        BoidAgent aliveBoid = FindBoid(false);
        if (aliveBoid != null && Time.time >= lastAttackTime + tba)
        {
            target = aliveBoid;
            ChangeState(HunterState.Attack);
            return;
        }

        interestSpawnTimer += Time.deltaTime;
        if (interestSpawnTimer >= interestSpawnInterval)
        {
            interestSpawnTimer = 0f;
            if (InterestManager.Instance != null && InterestManager.Instance.ActiveCount < 5)
            {
                InterestManager.Instance.SpawnInterest();
            }
        }

        PatrolMovement();
    }

    private void UpdateAttack()
    {
        currentEnergy -= energyConsumptionRate * Time.deltaTime;
        if (currentEnergy <= 0f)
        {
            currentEnergy = 0f;
            target = null;
            ChangeState(HunterState.Rest);
            return;
        }

        if (target == null)
        {
            ChangeState(HunterState.Patrol);
            return;
        }

        BoidHealth health = target.GetComponent<BoidHealth>();
        if (health == null || health.IsDead)
        {
            BoidAgent nextAlive = FindBoid(false);
            if (nextAlive != null)
            {
                target = nextAlive;
            }
            else
            {
                ChangeState(HunterState.Patrol);
            }
            return;
        }

        float distance = Vector3.Distance(transform.position, target.transform.position);

        if (distance > perceptionRadius + lostTargetTolerance)
        {
            target = null;
            ChangeState(HunterState.Patrol);
            return;
        }

        BoidAgent deadBoid = FindBoid(true);
        if (deadBoid != null)
        {
            gatherTarget = deadBoid;
            ChangeState(HunterState.Gather);
            return;
        }

        if (distance > rangeAttackRadius)
        {
            MoveTowards(target.transform.position);
            return;
        }

        if (Time.time < lastAttackTime + tba)
        {
            if (distance > meleeAttackRadius)
                MoveTowards(target.transform.position);
            else
                StopMovement();
            return;
        }

        if (distance <= meleeAttackRadius)
        {
            Debug.Log("Hunter: ataque cuerpo a cuerpo (2 hits)");
            health.TakeDamage(attackDamage * 2);
        }
        else
        {
            Debug.Log("Hunter: ataque a distancia (1 hit)");
            health.TakeDamage(attackDamage);
        }

        lastAttackTime = Time.time;

        if (visualFeedback != null)
            visualFeedback.ShowAttack();

        if (health.IsDead)
        {
            target = null;
            ChangeState(HunterState.Patrol);
        }
    }

    private void UpdateGather()
    {
        if (gatherTarget == null)
        {
            ChangeState(HunterState.Patrol);
            return;
        }

        BoidHealth health = gatherTarget.GetComponent<BoidHealth>();
        if (health == null || !health.IsDead)
        {
            gatherTarget = null;
            ChangeState(HunterState.Patrol);
            return;
        }

        float distance = Vector3.Distance(transform.position, gatherTarget.transform.position);
        if (distance > gatherDistance)
        {
            MoveTowards(gatherTarget.transform.position);
            return;
        }

        StopMovement();
        gatherTimer += Time.deltaTime;
        if (gatherTimer >= gatherTime)
        {
            gatherTimer = 0f;
            BoidAgent collectedBoid = gatherTarget;
            gatherTarget = null;

            collectedBoid.gameObject.SetActive(false);
            StartCoroutine(RespawnBoid(collectedBoid));

            ChangeState(HunterState.Patrol);
        }
    }

    private void UpdateRest()
    {
        StopMovement();

        currentEnergy += energyRechargeRate * Time.deltaTime;
        if (currentEnergy >= maxEnergy)
        {
            currentEnergy = maxEnergy;
            ChangeState(HunterState.Patrol);
        }
    }

    private BoidAgent FindBoid(bool findDead)
    {
        BoidAgent closest = null;
        float closestDistance = Mathf.Infinity;

        foreach (BoidAgent boid in detectedBoids)
        {
            if (boid == null) continue;

            BoidHealth health = boid.GetComponent<BoidHealth>();
            if (health == null || health.IsDead != findDead)
                continue;

            float distance = (transform.position - boid.transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = boid;
            }
        }
        return closest;
    }

    private void PatrolMovement()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            StopMovement();
            return;
        }

        Vector3 destination = waypoints[currentWaypoint].position;
        float distance = Vector3.Distance(transform.position, destination);

        if (distance <= waypointDistance)
        {
            if (waypoints.Length > 1)
            {
                if (currentWaypoint == waypoints.Length - 1)
                    waypointDirection = -1;
                else if (currentWaypoint == 0)
                    waypointDirection = 1;

                currentWaypoint += waypointDirection;
            }
            return;
        }

        MoveTowards(destination);
    }

    private void MoveTowards(Vector3 destination)
    {
        Vector3 direction = destination - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            StopMovement();
            return;
        }

        direction.Normalize();
        Velocity = direction * speed;
        transform.position += Velocity * Time.deltaTime;
        transform.forward = direction;
    }

    private void StopMovement()
    {
        Velocity = Vector3.zero;
    }

    private IEnumerator RespawnBoid(BoidAgent boid)
    {
        yield return new WaitForSeconds(respawnDelay);

        if (boid == null) yield break;

        boid.transform.position = new Vector3(
            Random.Range(-respawnWidth, respawnWidth),
            1f,
            Random.Range(-respawnHeight, respawnHeight)
        );

        BoidHealth health = boid.GetComponent<BoidHealth>();
        if (health != null)
            health.Revive();

        boid.InitializeMovement();
        boid.gameObject.SetActive(true);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, perceptionRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, rangeAttackRadius);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, meleeAttackRadius);
    }
}
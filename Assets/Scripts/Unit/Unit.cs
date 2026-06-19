using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

public enum UnitSide { Player, Enemy }
public enum UnitState { Idle, Moving, Attacking, Dead }
public enum DamageType { Physical, Magical }

// ─────────────────────────────────────────────────────────────────────────────
//  UNIT STATS
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class UnitStats
{
    [Header("Vida")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Combate")]
    public float attackDamage = 12f;
    public float defense = 5f;
    public float attackRange = 1.3f;
    public float attackCooldown = 1.5f;

    [Header("Movimiento")]
    public float moveSpeed = 3.5f;
    public float stoppingDist = 0.1f;
    [Range(0f, 1f)] public float hitSlowRatio = 0.33f;
    public float hitSlowDuration = 0.5f;

    [Header("Peso")]
    [Tooltip("Peso de la unidad. Afecta la ralentización por contacto")]
    public float weight = 1f;

    [Header("Attack Dash")]
    [Tooltip("Distancia máxima del dash en unidades locales del sprite")]
    public float dashDistance = 0.35f;
    [Tooltip("Duración total del dash (ida + vuelta)")]
    public float dashDuration = 0.12f;

    [Header("Bonificadores de posición")]
    [Range(1f, 3f)] public float flankMultiplier = 1.25f;
    [Range(1f, 3f)] public float backMultiplier = 1.50f;
    [Range(0f, 1f)] public float multiFocusBonus = 0.15f;

    public void ResetHealth() => currentHealth = maxHealth;
}

// ─────────────────────────────────────────────────────────────────────────────
//  UNIT BASE CLASS
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider2D))]
public class Unit : MonoBehaviour
{
    // ───────────────────────────────────────────────────────────────────────
    //  PUBLIC CONFIGURATION
    // ───────────────────────────────────────────────────────────────────────

    [Header("Identidad")]
    public string unitName = "Unit";
    public UnitSide side = UnitSide.Player;
    public UnitStats stats = new UnitStats();

    [Header("Referencias")]
    public SpriteRenderer spriteRenderer;

    // ───────────────────────────────────────────────────────────────────────
    //  STATE & PROPERTIES
    // ───────────────────────────────────────────────────────────────────────

    [Header("Estado (solo lectura en Inspector)")]
    [SerializeField] private UnitState _state = UnitState.Idle;

    public UnitState State
    {
        get => _state;
        protected set
        {
            if (_state == value) return;
            UnitState previousState = _state;
            _state = value;
            OnStateChanged(previousState, value);
            onStateChanged?.Invoke(value);
        }
    }

    public bool IsDead => State == UnitState.Dead;
    public Unit CurrentTarget { get; protected set; }

    // ───────────────────────────────────────────────────────────────────────
    //  EVENTS
    // ───────────────────────────────────────────────────────────────────────

    [Header("Eventos")]
    public UnityEvent<UnitState> onStateChanged;
    public UnityEvent<float, float> onHealthChanged;
    public UnityEvent<Unit> onDeath;
    public UnityEvent<float, Unit> onDamageReceived;
    public UnityEvent<float, Unit> onDamageDealt;

    // ───────────────────────────────────────────────────────────────────────
    //  PROTECTED STATE
    // ───────────────────────────────────────────────────────────────────────

    protected NavMeshAgent agent;
    protected float attackTimer = 0f;
    protected List<Unit> attackersOnMe = new List<Unit>();
    protected Color baseColor;

    protected List<Unit> contactingUnitsFromOtherSide = new List<Unit>();
    protected float contactSlowAmount = 1f;

    // ───────────────────────────────────────────────────────────────────────
    //  ROUTINES
    // ───────────────────────────────────────────────────────────────────────

    private Coroutine _slowRoutine;
    private Coroutine _dashRoutine;

    // ───────────────────────────────────────────────────────────────────────
    //  PATHPOINTS
    // ───────────────────────────────────────────────────────────────────────

    protected List<Vector3> currentPathPoints = new List<Vector3>();
    protected int currentWaypointIndex = 0;
    protected bool isFollowingPath = false;
    public bool IsFollowingPath => isFollowingPath;
    public int CurrentWaypointIndex => currentWaypointIndex;

    // ───────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ───────────────────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        ConfigureAgent();
        stats.ResetHealth();

        if (spriteRenderer != null)
            baseColor = spriteRenderer.color;

        GetComponent<Collider2D>().isTrigger = true;
    }

    protected virtual void Update()
    {
        if (IsDead) return;

        attackTimer -= Time.deltaTime;
        UpdateAgentSpeed();

        switch (State)
        {
            case UnitState.Idle: UpdateIdle(); break;
            case UnitState.Moving: UpdateMoving(); break;
            case UnitState.Attacking: UpdateAttacking(); break;
        }
    }

    protected virtual void LateUpdate()
    {

    }

    // ───────────────────────────────────────────────────────────────────────
    //  CONFIGURATION
    // ───────────────────────────────────────────────────────────────────────

    private void ConfigureAgent()
    {
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.speed = stats.moveSpeed;
        agent.stoppingDistance = stats.stoppingDist;
        agent.angularSpeed = 0f;
        agent.acceleration = 20f;
        agent.radius = 0.3f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  STATE UPDATES
    // ───────────────────────────────────────────────────────────────────────

    protected virtual void UpdateIdle() { }

    protected virtual void UpdateMoving()
    {
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (isFollowingPath)
            {
                currentWaypointIndex++;
                MoveToNextWaypoint();
            }
            else
            {
                agent.ResetPath();
                State = UnitState.Idle;
            }
            return;
        }

        FlipTowardVelocity();
    }

    protected virtual void UpdateAttacking()
    {
        if (CurrentTarget == null || CurrentTarget.IsDead)
        {
            ClearTarget();
            return;
        }

        float distanceToTarget = Vector2.Distance(transform.position, CurrentTarget.transform.position);

        if (distanceToTarget > stats.attackRange * 1.25f)
        {
            agent.SetDestination(CurrentTarget.transform.position);
            FlipTowardVelocity();
        }
        else
        {
            agent.ResetPath();
            FlipToward(CurrentTarget.transform.position);

            if (attackTimer <= 0f)
            {
                ExecuteAttack();
                attackTimer = stats.attackCooldown;
            }
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  PUBLIC ORDERS
    // ───────────────────────────────────────────────────────────────────────

    public virtual void OrderMoveTo(Vector3 destination)
    {
        if (IsDead) return;
        ClearTarget();
        agent.SetDestination(destination);
        State = UnitState.Moving;
    }

    public virtual void OrderMoveTo(Vector3 destination, Vector2 facingDirection)
    {
        OrderMoveTo(destination);
    }

    public virtual void OrderAttack(Unit target)
    {
        if (IsDead || target == null || target.IsDead) return;
        CurrentTarget = target;
        State = UnitState.Attacking;
    }

    public virtual void OrderStop()
    {
        if (IsDead) return;
        agent.ResetPath();
        ClearTarget();
        State = UnitState.Idle;
    }

    public virtual void OrderFollowPath(List<Vector2> pathPoints)
    {
        if (IsDead || pathPoints == null || pathPoints.Count == 0) return;

        ClearTarget();

        currentPathPoints = pathPoints.Select(p => new Vector3(p.x, p.y, 0f)).ToList();
        currentWaypointIndex = 0;
        isFollowingPath = true;

        MoveToNextWaypoint();
        State = UnitState.Moving;
    }

    protected void MoveToNextWaypoint()
    {
        if (currentWaypointIndex < currentPathPoints.Count)
        {
            agent.SetDestination(currentPathPoints[currentWaypointIndex]);
            State = UnitState.Moving;
        }
        else
        {
            isFollowingPath = false;
            State = UnitState.Idle;
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  COMBAT
    // ───────────────────────────────────────────────────────────────────────

    private void ExecuteAttack()
    {
        if (CurrentTarget == null || CurrentTarget.IsDead) return;

        float damage = CalculateDamage(CurrentTarget);
        CurrentTarget.ReceiveDamage(damage, this);
        onDamageDealt?.Invoke(damage, CurrentTarget);

        // Dash visual en local space (funciona para aliadas Y enemigas)
        if (_dashRoutine != null) StopCoroutine(_dashRoutine);
        _dashRoutine = StartCoroutine(AttackDashRoutine(CurrentTarget.transform.position));
    }

    private float CalculateDamage(Unit target)
    {
        float baseDamage = Mathf.Max(1f, stats.attackDamage - target.stats.defense * 0.5f);
        float focusBonus = CalculateFocusBonus(target);
        float angleBonus = CalculateAngleBonus(target);
        return baseDamage * focusBonus * angleBonus;
    }

    private float CalculateFocusBonus(Unit target)
    {
        int extraAttackers = Mathf.Max(0, target.attackersOnMe.Count - 1);
        return 1f + extraAttackers * target.stats.multiFocusBonus;
    }

    private float CalculateAngleBonus(Unit target)
    {
        Vector2 targetFacing = target.GetFacingDirection();
        Vector2 attackDirection = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
        float dot = Vector2.Dot(targetFacing, attackDirection);

        if (dot > 0.5f) return target.stats.backMultiplier;
        if (dot > -0.5f) return target.stats.flankMultiplier;
        return 1f;
    }

    public virtual void ReceiveDamage(float amount, Unit attacker)
    {
        if (IsDead) return;

        if (attacker != null && !attackersOnMe.Contains(attacker))
            attackersOnMe.Add(attacker);

        stats.currentHealth -= amount;
        stats.currentHealth = Mathf.Max(stats.currentHealth, 0f);

        if (_slowRoutine != null) StopCoroutine(_slowRoutine);
        _slowRoutine = StartCoroutine(HitSlowRoutine());

        onHealthChanged?.Invoke(stats.currentHealth, stats.maxHealth);
        onDamageReceived?.Invoke(amount, attacker);

        StartCoroutine(FlashDamage());

        if (stats.currentHealth <= 0f)
            Die();
    }

    public virtual void Heal(float amount)
    {
        if (IsDead) return;
        stats.currentHealth = Mathf.Min(stats.currentHealth + amount, stats.maxHealth);
        onHealthChanged?.Invoke(stats.currentHealth, stats.maxHealth);
    }

    // ───────────────────────────────────────────────────────────────────────
    //  COROUTINES
    // ───────────────────────────────────────────────────────────────────────

    private IEnumerator HitSlowRoutine()
    {
        agent.speed = stats.moveSpeed * stats.hitSlowRatio;
        yield return new WaitForSeconds(stats.hitSlowDuration);
        agent.speed = stats.moveSpeed;
        _slowRoutine = null;
    }

    /// <summary>
    /// Dash visual que mueve solo el SPRITE en coordenadas LOCAL del GameObject.
    /// El NavMeshAgent sigue moviendo transform.position sin interferir.
    /// </summary>
    private IEnumerator AttackDashRoutine(Vector3 targetWorldPos)
    {
        if (spriteRenderer == null) yield break;

        Transform spriteTransform = spriteRenderer.transform;

        // Guardamos la posición local original (normalmente Vector3.zero)
        Vector3 originalLocalPos = spriteTransform.localPosition;

        // Calculamos la dirección hacia el target en local space del padre
        // Convertimos targetWorldPos a local space del transform padre
        Vector3 targetLocalPos = transform.InverseTransformPoint(targetWorldPos);
        targetLocalPos.z = originalLocalPos.z; // mantener Z local

        // Limitamos la distancia del dash
        Vector3 dashDir = targetLocalPos - originalLocalPos;
        float cappedDist = Mathf.Min(stats.dashDistance, dashDir.magnitude * 0.45f);
        Vector3 peakLocalPos = originalLocalPos + dashDir.normalized * cappedDist;

        float half = stats.dashDuration * 0.5f;
        float t = 0f;

        // IDA — local space, el agente puede mover el padre sin problema
        while (t < half)
        {
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, t / half);
            spriteTransform.localPosition = Vector3.LerpUnclamped(originalLocalPos, peakLocalPos, progress);
            yield return null;
        }

        // VUELTA — volvemos al origen local (siempre el mismo, no depende del mundo)
        t = 0f;
        Vector3 returnStart = spriteTransform.localPosition;
        while (t < half)
        {
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, t / half);
            spriteTransform.localPosition = Vector3.LerpUnclamped(returnStart, originalLocalPos, progress);
            yield return null;
        }

        // Restaurar exactamente
        spriteTransform.localPosition = originalLocalPos;
        _dashRoutine = null;
    }

    /// <summary>Flash negro al recibir daño.</summary>
    protected IEnumerator FlashDamage()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.color = Color.black;
        yield return new WaitForSeconds(0.07f);
        spriteRenderer.color = baseColor;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  DEATH
    // ───────────────────────────────────────────────────────────────────────

    protected virtual void Die()
    {
        State = UnitState.Dead;
        agent.ResetPath();
        agent.enabled = false;

        foreach (var otherUnit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            otherUnit.attackersOnMe.Remove(this);
            otherUnit.contactingUnitsFromOtherSide.Remove(this);
        }

        onDeath?.Invoke(this);
        StartCoroutine(DieRoutine());
    }

    private IEnumerator DieRoutine()
    {
        float duration = 0.6f;
        float elapsed = 0f;
        Color startColor = spriteRenderer != null ? spriteRenderer.color : Color.white;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;
            if (spriteRenderer != null)
            {
                Color newColor = startColor;
                newColor.a = Mathf.Lerp(startColor.a, 0f, progress);
                spriteRenderer.color = newColor;
            }
            yield return null;
        }

        Destroy(gameObject);
    }

    // ───────────────────────────────────────────────────────────────────────
    //  UTILITIES
    // ───────────────────────────────────────────────────────────────────────

    public virtual Vector2 GetFacingDirection() => transform.right;

    public virtual void OnStateChanged(UnitState oldState, UnitState newState) { }

    protected void FlipToward(Vector3 target)
    {
        Vector2 direction = (target - transform.position).normalized;
        if (direction.sqrMagnitude > 0.001f)
            RotateTowards(direction);
    }

    protected void FlipTowardVelocity()
    {
        if (agent == null || agent.velocity.sqrMagnitude < 0.01f) return;
        RotateTowards(agent.velocity.normalized);
    }

    protected virtual void RotateTowards(Vector2 direction)
    {
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    protected void ClearTarget()
    {
        isFollowingPath = false;
        currentWaypointIndex = 0;

        if (CurrentTarget != null)
        {
            if (CurrentTarget.attackersOnMe.Contains(this))
                CurrentTarget.attackersOnMe.Remove(this);
            CurrentTarget = null;
        }

        if (State != UnitState.Moving)
            State = UnitState.Idle;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  DEBUG GIZMOS
    // ───────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stats.attackRange);

        Gizmos.color = Color.cyan;
        Vector2 facing = Application.isPlaying ? GetFacingDirection() : Vector2.right;
        Gizmos.DrawLine(transform.position, transform.position + (Vector3)(facing * 0.8f));

        if (Application.isPlaying && CurrentTarget != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, CurrentTarget.transform.position);
        }
    }
#endif
}
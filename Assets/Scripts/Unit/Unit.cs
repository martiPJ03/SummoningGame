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

/// <summary>
/// Estadísticas de combate para una unidad.
/// Serializable para permitir edición en Inspector y clonación de prefabs.
/// </summary>
[System.Serializable]
public class UnitStats
{
    [Header("Vida")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Combate")]
    public float attackDamage = 12f;
    public float defense = 5f;    // reduce daño físico recibido
    public float attackRange = 1.3f;  // metros
    public float attackCooldown = 1.5f;  // segundos entre ataques

    [Header("Movimiento")]
    public float moveSpeed = 3.5f;  // unidades/s
    public float stoppingDist = 0.1f;  // distancia de parada del agente

    [Header("Bonificadores de posición")]
    [Range(1f, 3f)] public float flankMultiplier = 1.25f;  // ataque lateral
    [Range(1f, 3f)] public float backMultiplier = 1.50f;  // ataque por la espalda
    [Range(0f, 1f)] public float multiFocusBonus = 0.15f;  // por cada atacante extra

    public void ResetHealth() => currentHealth = maxHealth;
}

// ─────────────────────────────────────────────────────────────────────────────
//  UNIT BASE CLASS
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Clase base para todas las unidades (aliadas y enemigas).
/// Responsabilidades:
///   · Gestión de estados (Idle, Moving, Attacking, Dead)
///   · Lógica de combate (daño, curación, muerte)
///   · Rotación del sprite y movimiento básico
/// 
/// Las subclases manejan:
///   · Órdenes específicas del jugador (AlliedUnit)
///   · Comportamiento IA (EnemyUnit)
///   · Feedback visual (OrderIndicators, selección, etc.)
/// </summary>
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

    /// <summary>Estado actual de la unidad.</summary>
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

    /// <summary>¿Está muerta la unidad?</summary>
    public bool IsDead => State == UnitState.Dead;

    /// <summary>Objetivo actual en combate (null si no hay target).</summary>
    public Unit CurrentTarget { get; protected set; }

    // ───────────────────────────────────────────────────────────────────────
    //  EVENTS
    // ───────────────────────────────────────────────────────────────────────

    [Header("Eventos")]
    public UnityEvent<UnitState> onStateChanged;        // cambio de estado
    public UnityEvent<float, float> onHealthChanged;    // (salud actual, máx)
    public UnityEvent<Unit> onDeath;                    // muerte
    public UnityEvent<float, Unit> onDamageReceived;    // (daño, atacante)
    public UnityEvent<float, Unit> onDamageDealt;       // (daño, objetivo)

    // ───────────────────────────────────────────────────────────────────────
    //  PROTECTED STATE (para subclases)
    // ───────────────────────────────────────────────────────────────────────

    protected NavMeshAgent agent;
    protected float attackTimer = 0f;
    protected List<Unit> attackersOnMe = new List<Unit>();
    protected Color baseColor;


    // ───────────────────────────────────────────────────────────────────────
    //  PATHPOINTS
    // ───────────────────────────────────────────────────────────────────────
    
    protected List<Vector3> currentPathPoints = new List<Vector3>();
    protected int currentWaypointIndex = 0;
    protected bool isFollowingPath = false;
    public bool IsFollowingPath => isFollowingPath;
    public int CurrentWaypointIndex => currentWaypointIndex; // ← añadir esto

    // ───────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ───────────────────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        // Configurar NavMeshAgent
        agent = GetComponent<NavMeshAgent>();
        ConfigureAgent();

        // Inicializar salud
        stats.ResetHealth();

        // Guardar color base para flashes
        if (spriteRenderer != null)
            baseColor = spriteRenderer.color;
    }

    protected virtual void Update()
    {
        if (IsDead) return;

        // Decrementar timer de ataque
        attackTimer -= Time.deltaTime;

        // Actualizar lógica de estado
        switch (State)
        {
            case UnitState.Idle:
                UpdateIdle();
                break;
            case UnitState.Moving:
                UpdateMoving();
                break;
            case UnitState.Attacking:
                UpdateAttacking();
                break;
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  CONFIGURATION
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>Configura el NavMeshAgent con parámetros óptimos para RTS.</summary>
    private void ConfigureAgent()
    {
        agent.updateRotation = false;  // Nosotros controlamos la rotación
        agent.updateUpAxis = false;  // Requerido para NavMesh 2D
        agent.speed = stats.moveSpeed;
        agent.stoppingDistance = stats.stoppingDist;
        agent.angularSpeed = 0f;
        agent.acceleration = 20f;
        agent.radius = 0.3f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  STATE UPDATES (virtuales para personalización en subclases)
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>Actualización cuando la unidad está en reposo.</summary>
    protected virtual void UpdateIdle()
    {
        // Las subclases pueden agregar lógica aquí (auto-aggro, patrulla, etc.)
    }

    /// <summary>Actualización cuando la unidad se está moviendo.</summary>
    protected virtual void UpdateMoving()
    {
        Debug.Log($"{unitName} está en UpdateMoving. Distancia restante: {agent.remainingDistance}");
        // Si llegó al destino actual
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (isFollowingPath)
            {
                // Avanzamos al siguiente punto
                currentWaypointIndex++;
                Debug.Log($"Unit {unitName} reached waypoint {currentWaypointIndex}/{currentPathPoints.Count}");
                MoveToNextWaypoint();
            }
            else
            {
                // No hay ruta, nos quedamos quietos
                agent.ResetPath();
                Debug.Log($"Unit {unitName} reached destination, switching to Idle");
                State = UnitState.Idle;
            }
            return;
        }

        // Girar sprite hacia dirección del movimiento
        FlipTowardVelocity();
    }

    /// <summary>Actualización cuando la unidad está atacando.</summary>
    protected virtual void UpdateAttacking()
    {
        // Target inválido o muerto → volver a Idle
        if (CurrentTarget == null || CurrentTarget.IsDead)
        {
            ClearTarget();
            return;
        }

        float distanceToTarget = Vector2.Distance(transform.position, CurrentTarget.transform.position);

        if (distanceToTarget > stats.attackRange * 1.25f)
        {
            // Fuera de rango → perseguir
            agent.SetDestination(CurrentTarget.transform.position);
            FlipTowardVelocity();
        }
        else
        {
            // En rango → atacar
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
    //  PUBLIC ORDERS (órdenes básicas, pueden ser sobrescritas)
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>Mover a una posición del mundo (versión simple sin facing).</summary>
    public virtual void OrderMoveTo(Vector3 destination)
    {
        if (IsDead) return;
        ClearTarget();
        agent.SetDestination(destination);
        State = UnitState.Moving;
    }

    /// <summary>Mover a una posición del mundo con facing direction deseado.</summary>
    public virtual void OrderMoveTo(Vector3 destination, Vector2 facingDirection)
    {
        // Versión sobrecargada. Las subclases pueden personalizarla.
        OrderMoveTo(destination);
    }

    /// <summary>Atacar un objetivo concreto.</summary>
    public virtual void OrderAttack(Unit target)
    {
        if (IsDead || target == null || target.IsDead) return;
        CurrentTarget = target;
        State = UnitState.Attacking;
    }

    /// <summary>Detener toda acción y quedarse en Idle.</summary>
    public virtual void OrderStop()
    {
        if (IsDead) return;
        agent.ResetPath();
        ClearTarget();
        State = UnitState.Idle;
    }

    /// <summary>
    /// Orden de seguir un path definido por una lista de puntos.
    /// La unidad recorrerá todos los puntos en orden, pasando por cada waypoint.
    /// </summary>
    public virtual void OrderFollowPath(List<Vector2> pathPoints)
    {
        if (IsDead || pathPoints == null || pathPoints.Count == 0) return;
        
        State = UnitState.Moving;

        ClearTarget();

        // Guardamos la ruta
        currentPathPoints = pathPoints.Select(p => new Vector3(p.x, p.y, 0f)).ToList();

        currentWaypointIndex = 0;
        isFollowingPath = true;

        // Iniciamos el movimiento al primer punto
        MoveToNextWaypoint();
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
            // Hemos llegado al final
            isFollowingPath = false;
            State = UnitState.Idle;
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  COMBAT
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>Ejecuta un ataque contra el objetivo actual.</summary>
    private void ExecuteAttack()
    {
        if (CurrentTarget == null || CurrentTarget.IsDead) return;

        float damage = CalculateDamage(CurrentTarget);
        CurrentTarget.ReceiveDamage(damage, this);
        onDamageDealt?.Invoke(damage, CurrentTarget);

        StartCoroutine(FlashAttack());
    }

    /// <summary>Calcula el daño infligido considerando defensas y bonificadores.</summary>
    private float CalculateDamage(Unit target)
    {
        // Daño base (mínimo 1)
        float baseDamage = Mathf.Max(1f, stats.attackDamage - target.stats.defense * 0.5f);

        // Bonificadores
        float focusBonus = CalculateFocusBonus(target);
        float angleBonus = CalculateAngleBonus(target);

        return baseDamage * focusBonus * angleBonus;
    }

    /// <summary>Calcula el bonus por focus-fire (múltiples atacantes).</summary>
    private float CalculateFocusBonus(Unit target)
    {
        int extraAttackers = Mathf.Max(0, target.attackersOnMe.Count - 1);
        return 1f + extraAttackers * target.stats.multiFocusBonus;
    }

    /// <summary>Calcula el bonus por ángulo de ataque (flanco, espalda, frente).</summary>
    private float CalculateAngleBonus(Unit target)
    {
        Vector2 targetFacing = target.GetFacingDirection();
        Vector2 attackDirection = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
        float dot = Vector2.Dot(targetFacing, attackDirection);

        // dot > 0.5  → ataque por la espalda
        // 0.5 > dot > -0.5 → ataque lateral (flanco)
        // dot < -0.5 → ataque frontal

        if (dot > 0.5f) return target.stats.backMultiplier;
        if (dot > -0.5f) return target.stats.flankMultiplier;
        return 1f;
    }

    /// <summary>Recibir daño de un atacante.</summary>
    public virtual void ReceiveDamage(float amount, Unit attacker)
    {
        if (IsDead) return;

        // Registrar atacante para bonus de focus-fire
        if (attacker != null && !attackersOnMe.Contains(attacker))
            attackersOnMe.Add(attacker);

        // Aplicar daño
        stats.currentHealth -= amount;
        stats.currentHealth = Mathf.Max(stats.currentHealth, 0f);

        // Eventos
        onHealthChanged?.Invoke(stats.currentHealth, stats.maxHealth);
        onDamageReceived?.Invoke(amount, attacker);

        // Feedback visual
        StartCoroutine(FlashDamage());

        // Morir si salud <= 0
        if (stats.currentHealth <= 0f)
            Die();
    }

    /// <summary>Curar a la unidad.</summary>
    public virtual void Heal(float amount)
    {
        if (IsDead) return;
        stats.currentHealth = Mathf.Min(stats.currentHealth + amount, stats.maxHealth);
        onHealthChanged?.Invoke(stats.currentHealth, stats.maxHealth);
    }

    // ───────────────────────────────────────────────────────────────────────
    //  DEATH
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>Muere la unidad.</summary>
    protected virtual void Die()
    {
        State = UnitState.Dead;
        agent.ResetPath();
        agent.enabled = false;

        // Limpiar referencias en otros atacantes
        foreach (var otherUnit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
            otherUnit.attackersOnMe.Remove(this);

        onDeath?.Invoke(this);
        StartCoroutine(DieRoutine());
    }

    /// <summary>Rutina de muerte (fade-out).</summary>
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

    /// <summary>Obtiene la dirección hacia la que mira la unidad.</summary>
    public virtual Vector2 GetFacingDirection()
    {
        // transform.right es el eje X rotado, que indica la dirección de facing
        return transform.right;
    }

    /// <summary>Llama al método OnStateChanged. Puede ser sobrescrito por subclases.</summary>
    public virtual void OnStateChanged(UnitState oldState, UnitState newState)
    {
        // Lógica opcional para ser implementada por subclases
    }

    /// <summary>Girar sprite hacia un objetivo.</summary>
    protected void FlipToward(Vector3 target)
    {
        Vector2 direction = (target - transform.position).normalized;
        if (direction.sqrMagnitude > 0.001f)
            RotateTowards(direction);
    }

    /// <summary>Girar sprite hacia la dirección del movimiento del NavMeshAgent.</summary>
    protected void FlipTowardVelocity()
    {
        if (agent == null || agent.velocity.sqrMagnitude < 0.01f) return;
        RotateTowards(agent.velocity.normalized);
    }

    /// <summary>Rota el sprite en una dirección.</summary>
    protected virtual void RotateTowards(Vector2 direction)
    {
        // Calcular ángulo
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    /// <summary>Limpia el objetivo actual.</summary>
    protected void ClearTarget()
    {
        // Reseteamos el estado del path cada vez que recibimos una orden nueva
        isFollowingPath = false;
        currentWaypointIndex = 0;

        if (CurrentTarget != null)
        {
            if (CurrentTarget.attackersOnMe.Contains(this))
                CurrentTarget.attackersOnMe.Remove(this);
            CurrentTarget = null;
        }

        // Solo pasamos a Idle si no estamos en medio de un movimiento 
        // (opcional, dependiendo de cómo quieras que se sienta el control)
        if (State != UnitState.Moving) State = UnitState.Idle;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  VISUAL FEEDBACK
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>Flash amarillo al atacar.</summary>
    protected IEnumerator FlashAttack()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.color = Color.yellow;
        yield return new WaitForSeconds(0.08f);
        spriteRenderer.color = baseColor;
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
    //  DEBUG GIZMOS
    // ───────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    protected virtual void OnDrawGizmosSelected()
    {
        // Rango de ataque
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stats.attackRange);

        // Dirección de facing
        Gizmos.color = Color.cyan;
        Vector2 facing = Application.isPlaying ? GetFacingDirection() : Vector2.right;
        Gizmos.DrawLine(transform.position, transform.position + (Vector3)(facing * 0.8f));

        // Línea hacia el objetivo
        if (Application.isPlaying && CurrentTarget != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, CurrentTarget.transform.position);
        }
    }
#endif
}

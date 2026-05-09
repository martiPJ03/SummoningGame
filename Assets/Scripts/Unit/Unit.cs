using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using static UnityEngine.CullingGroup;

public enum UnitSide { Player, Enemy }
public enum UnitState { Idle, Moving, Attacking, Dead }
public enum DamageType { Physical, Magical }

// ─────────────────────────────────────────────────────────────────────────────
//  STATS  (serializable para editar en Inspector y clonar fácilmente)
// ─────────────────────────────────────────────────────────────────────────────

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
    public float moveSpeed = 3.5f;  // unidades/s (se aplica al NavMeshAgent)
    public float stoppingDist = 0.1f;  // distancia de parada del agente

    [Header("Mana (solo summons jugador)")]
    public float manaPerSecond = 2f;    // coste de mantenimiento

    [Header("Bonificadores de posición")]
    [Range(1f, 3f)] public float flankMultiplier = 1.25f;  // ataque lateral
    [Range(1f, 3f)] public float backMultiplier = 1.50f;  // ataque por la espalda
    [Range(0f, 1f)] public float multiFocusBonus = 0.15f;  // por cada atacante extra

    public void ResetHealth() => currentHealth = maxHealth;
}

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider2D))]
public class Unit : MonoBehaviour
{
    [Header("Identidad")]
    public string unitName = "Unit";
    public UnitSide side = UnitSide.Player;
    public UnitStats stats = new UnitStats();

    [Header("Referencias")]
    public SpriteRenderer spriteRenderer;
    public GameObject selectionIndicator;   // círculo bajo el sprite
    public Transform healthBarPivot;       // pivot del world-space HP bar

    [Header("Estado (solo lectura en Inspector)")]
    [SerializeField] private UnitState _state = UnitState.Idle;

    public UnitState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            UnitState previous = _state;
            _state = value;
            OnStateChanged(previous, value);
            onStateChanged?.Invoke(value);
        }
    }

    public bool IsDead => State == UnitState.Dead;
    public bool IsSelected { get; private set; }
    public Unit CurrentTarget { get; private set; }
    public virtual void OnStateChanged(UnitState oldState, UnitState newState)
    {
        // Sin lógica adicional, listo para ser sobreescrito o expandido
    }

    // ── Eventos públicos ──────────────────────────────────────────────────────

    [Header("Eventos")]
    public UnityEvent<UnitState> onStateChanged;   // cada vez que cambia el estado
    public UnityEvent<float, float> onHealthChanged;  // (actual, máximo)
    public UnityEvent<Unit> onDeath;          // al morir, pasa referencia a sí mismo
    public UnityEvent<float, Unit> onDamageReceived; // (cantidad, atacante)
    public UnityEvent<float, Unit> onDamageDealt;    // (cantidad, objetivo)

    // ── Privado ───────────────────────────────────────────────────────────────

    protected NavMeshAgent agent;
    private UnitLayerSetup layerSetup;
    private float attackTimer = 0f;
    private List<Unit> attackersOnMe = new List<Unit>();  // para focus-fire bonus
    private Color baseColor;
    private Vector2 _desiredFacing = Vector2.zero;  // (0,0) = sin facing forzado

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        ConfigureAgent();

        stats.ResetHealth();

        if (spriteRenderer != null)
            baseColor = spriteRenderer.color;

        if (selectionIndicator != null)
            selectionIndicator.SetActive(false);

        onDeath.AddListener(unit =>
        {
            if (unit.side == UnitSide.Player)
                SelectionManager.Instance?.RemoveFromSelection(unit);
        });
    }

    protected virtual void Update()
    {
        if (IsDead) return;

        attackTimer -= Time.deltaTime;

        switch (State)
        {
            case UnitState.Idle: UpdateIdle(); break;
            case UnitState.Moving: UpdateMoving(); break;
            case UnitState.Attacking: UpdateAttacking(); break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONFIGURACIÓN NAVMESH
    // ─────────────────────────────────────────────────────────────────────────

    void ConfigureAgent()
    {
        agent.updateRotation = false;  // nosotros controlamos el sprite flip
        agent.updateUpAxis = false;  // requerido para NavMesh 2D
        agent.speed = stats.moveSpeed;
        agent.stoppingDistance = stats.stoppingDist;
        agent.angularSpeed = 0f;
        agent.acceleration = 20f;   // respuesta ágil, sensación RTS
        agent.radius = 0.3f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  STATES — métodos que las subclases pueden sobreescribir
    // ─────────────────────────────────────────────────────────────────────────

    protected virtual void UpdateIdle()
    {
        // Las subclases añaden aquí lógica de auto-aggro o patrulla
    }

    protected virtual void UpdateMoving()
    {
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            // Aplicar facing deseado antes de pasar a Idle
            if (_desiredFacing != Vector2.zero && spriteRenderer != null)
                RotateTowards(_desiredFacing);

            _desiredFacing = Vector2.zero;
            agent.ResetPath();
            State = UnitState.Idle;
            return;
        } else
        {
            FlipTowardVelocity();
        }
    }

    protected virtual void UpdateAttacking()
    {
        // Target inválido → volver a idle
        if (CurrentTarget == null || CurrentTarget.IsDead)
        {
            ClearTarget();
            return;
        }

        float dist = Vector2.Distance(transform.position, CurrentTarget.transform.position);

        if (dist > stats.attackRange * 1.25f)
        {
            // Perseguir al objetivo
            agent.SetDestination(CurrentTarget.transform.position);
            FlipTowardVelocity();
        }
        else
        {
            // En rango → detener y atacar
            agent.ResetPath();
            FlipToward(CurrentTarget.transform.position);
            if (attackTimer <= 0f)
            {
                ExecuteAttack();
                attackTimer = stats.attackCooldown;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ÓRDENES PÚBLICAS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Mover a una posición del mundo. Cancela objetivo actual.</summary>
    public void OrderMoveTo(Vector3 destination)
    {
        if (IsDead) return;
        ClearTarget();
        agent.SetDestination(destination);
        State = UnitState.Moving;
    }

    /// <summary>Atacar un objetivo concreto.</summary>
    public void OrderAttack(Unit target)
    {
        if (IsDead || target == null || target.IsDead) return;
        CurrentTarget = target;
        State = UnitState.Attacking;
    }

    /// <summary>Detener toda acción y quedarse idle.</summary>
    public void OrderStop()
    {
        if (IsDead) return;
        agent.ResetPath();
        ClearTarget();
        State = UnitState.Idle;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  COMBATE
    // ─────────────────────────────────────────────────────────────────────────

    void ExecuteAttack()
    {
        if (CurrentTarget == null || CurrentTarget.IsDead) return;

        float damage = CalculateDamage(CurrentTarget);
        CurrentTarget.ReceiveDamage(damage, this);
        onDamageDealt?.Invoke(damage, CurrentTarget);

        StartCoroutine(FlashAttack());
    }

    float CalculateDamage(Unit target)
    {
        float base_dmg = Mathf.Max(1f, stats.attackDamage - target.stats.defense * 0.5f);

        int extraAttackers = Mathf.Max(0, target.attackersOnMe.Count - 1);
        float focusBonus = CalculateFocusBonus(target);
        float angleBonus = CalculateAngleBonus(target);

        return base_dmg * focusBonus * angleBonus;
    }

    float CalculateFocusBonus(Unit target)
    {
        int extraAttackers = Mathf.Max(0, target.attackersOnMe.Count - 1);
        return 1f + extraAttackers * target.stats.multiFocusBonus;
    }

    float CalculateAngleBonus(Unit target)
    {
        Vector2 targetFacing = target.GetFacingDirection();
        Vector2 attackDirection = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;

        // dot > 0.5  → miran en misma direccion -> ataque por la espalda
        // dot ≈ 0    → ataque lateral (flanco)
        // dot < -0.5 → miran en direccion opuesta -> ataque frontal
        float dot = Vector2.Dot(targetFacing, attackDirection);

        if (dot > 0.5f) return target.stats.backMultiplier;
        if (dot < 0.5f && dot > -0.5f) return target.stats.flankMultiplier;
        return 1f;
    }

    /// <summary>Recibir daño de un atacante.</summary>
    public void ReceiveDamage(float amount, Unit attacker)
    {
        if (IsDead) return;

        // Registrar atacante para el bonus de focus-fire
        if (attacker != null && !attackersOnMe.Contains(attacker))
            attackersOnMe.Add(attacker);

        stats.currentHealth -= amount;
        stats.currentHealth = Mathf.Max(stats.currentHealth, 0f);

        onHealthChanged?.Invoke(stats.currentHealth, stats.maxHealth);
        onDamageReceived?.Invoke(amount, attacker);

        StartCoroutine(FlashDamage());

        if (stats.currentHealth <= 0f)
        {
            Die();
        }
    }
    /// <summary>Curar a la unidad.</summary>
    public void Heal(float amount)
    {
        if (IsDead) return;
        stats.currentHealth = Mathf.Min(stats.currentHealth + amount, stats.maxHealth);
        onHealthChanged?.Invoke(stats.currentHealth, stats.maxHealth);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MUERTE
    // ─────────────────────────────────────────────────────────────────────────

    protected virtual void Die()
    {
        State = UnitState.Dead;
        agent.ResetPath();
        agent.enabled = false;

        // Limpiar referencias en los atacantes que tenían esta unidad como target
        foreach (var otherUnit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
            otherUnit.attackersOnMe.Remove(this);

        onDeath?.Invoke(this);

        StartCoroutine(DieRoutine());
    }

    IEnumerator DieRoutine()
    {
        // Fade-out sencillo; sustituir por animación cuando haya sprites
        float duration = 0.6f;
        float elapsed = 0f;
        Color startColor = spriteRenderer != null ? spriteRenderer.color : Color.white;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;
            if (spriteRenderer != null)
                spriteRenderer.color = Color.Lerp(startColor,
                    new Color(startColor.r, startColor.g, startColor.b, 0f), progress);
            yield return null;
        }

        Destroy(gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SELECCIÓN
    // ─────────────────────────────────────────────────────────────────────────

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        if (selectionIndicator != null)
            selectionIndicator.SetActive(selected);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UTILIDADES
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Dirección hacia la que mira la unidad según el flip del sprite.</summary>

    public virtual Vector2 GetFacingDirection()
    {
        // El eje "derecha" del transform siempre apunta a donde mira el objeto si lo rotamos
        return transform.right;
    }

    void FlipToward(Vector3 target)
    {
        Vector2 direction = (target - transform.position).normalized;

        if (direction.sqrMagnitude > 0.001f)
        {
            RotateTowards(direction);
        }
    }

    void FlipTowardVelocity()
    {
        if (agent == null || agent.velocity.sqrMagnitude < 0.01f) return;

        RotateTowards(agent.velocity.normalized);
    }

    private void RotateTowards(Vector2 direction)
    {
        // Calculamos el ángulo en grados usando arcotangente
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // Aplicamos la rotación en el eje Z
        transform.rotation = Quaternion.Euler(0, 0, angle);
        // Evitar que el indicador de selección rote junto al sprite: mantenerlo alineado con el mundo
        if (selectionIndicator != null)
            selectionIndicator.transform.rotation = Quaternion.identity;
    }

    /// <summary>Mover a destino y al llegar orientar el sprite hacia facingDir.</summary>
    public void OrderMoveTo(Vector3 destination, Vector2 facingDir = default)
    {
        if (IsDead) return;
        ClearTarget();
        _desiredFacing = facingDir;
        agent.SetDestination(destination);
        State = UnitState.Moving;
    }

    void ClearTarget()
    {
        if (CurrentTarget != null)
        {
            if (CurrentTarget.attackersOnMe.Contains(this))
                CurrentTarget.attackersOnMe.Remove(this);
            CurrentTarget = null;
            State = UnitState.Idle;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  FEEDBACK VISUAL (flashes sin Animator)
    // ─────────────────────────────────────────────────────────────────────────

    IEnumerator FlashAttack()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.color = Color.yellow;
        yield return new WaitForSeconds(0.08f);
        spriteRenderer.color = baseColor;
    }

    IEnumerator FlashDamage()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.color = Color.black;
        yield return new WaitForSeconds(0.07f);
        spriteRenderer.color = baseColor;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GIZMOS (ayuda en editor)
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    protected virtual void OnDrawGizmosSelected()
    {
        // Rango de ataque
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stats.attackRange);

        // Dirección de facing
        Gizmos.color = Color.cyan;
        Vector2 facing = Application.isPlaying
            ? GetFacingDirection()
            : Vector2.right;
        Gizmos.DrawLine(transform.position,
            transform.position + (Vector3)(facing * 0.8f));

        // Línea hasta el objetivo actual
        if (Application.isPlaying && CurrentTarget != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, CurrentTarget.transform.position);
        }
    }
#endif
}

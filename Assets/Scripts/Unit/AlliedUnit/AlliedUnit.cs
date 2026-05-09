using UnityEngine;

/// <summary>
/// Unidad aliada del jugador. Extiende Unit agregando:
///   · Órdenes de movimiento con facing direction deseado
///   · Integración con SelectionManager para selección
///   · Visualización de órdenes (OrderIndicator)
///   · Feedback visual específico del jugador
/// 
/// Esta clase aplica todas las funcionalidades específicas del jugador,
/// manteniendo Unit limpia y centrada en lógica base.
/// </summary>
public class AlliedUnit : Unit
{
    // ───────────────────────────────────────────────────────────────────────
    //  CONFIGURATION
    // ───────────────────────────────────────────────────────────────────────
    [Header("Selection")]
    public GameObject selectionIndicator;   // círculo bajo el sprite

    [Header("Debug")]
    public bool showDebugGizmos = false;

    // ───────────────────────────────────────────────────────────────────────
    //  PRIVATE STATE
    // ───────────────────────────────────────────────────────────────────────

    // Facing direction deseado al llegar al destino
    private Vector2 desiredFacingDirection = Vector2.zero;

    // ─────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        // Ocultar indicador de selección por defecto
        if (selectionIndicator != null)
            selectionIndicator.SetActive(false);

        // Registrar evento de muerte para limpiar la selección en SelectionManager
        onDeath.AddListener(deadUnit =>
        {
            SelectionManager.Instance?.RemoveFromSelection(deadUnit as AlliedUnit);
        });
    }

    // ─────────────────────────────────────────────────────────────────────
    //  SELECTION
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Mostrar/ocultar indicador de selección.</summary>
    public void SetSelected(bool selected)
    {
        if (selectionIndicator != null)
            selectionIndicator.SetActive(selected);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  PLAYER ORDERS (órdenes del jugador)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mover a destino con orientación deseada.
    /// Al llegar, la unidad girará hacia facingDirection.
    /// </summary>
    public override void OrderMoveTo(Vector3 destination, Vector2 facingDirection = default)
    {
        if (IsDead) return;
        ClearTarget();
        desiredFacingDirection = facingDirection;
        agent.SetDestination(destination);
        State = UnitState.Moving;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  STATE UPDATES (personalizados para AlliedUnit)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Actualización de movimiento personalizada.
    /// Aplica facing deseado al llegar al destino.
    /// </summary>
    protected override void UpdateMoving()
    {
        // 1. Si el NavMesh aún está calculando, esperamos
        if (agent.pathPending) return;

        // 2. ¿Hemos llegado al punto actual?
        if (agent.remainingDistance <= agent.stoppingDistance + 0.2f)
        {
            // CASO A: Estamos siguiendo un Path (lista de puntos)
            if (isFollowingPath)
            {
                currentWaypointIndex++;

                // Si quedan más puntos, vamos al siguiente y salimos del método
                if (currentWaypointIndex < currentPathPoints.Count)
                {
                    MoveToNextWaypoint();
                    return;
                }

                // Si era el último punto, limpiamos el estado de path y seguimos al giro final
                isFollowingPath = false;
            }

            // Aplicar rotación deseada si existe
            if (desiredFacingDirection != Vector2.zero)
                RotateTowards(desiredFacingDirection);

            desiredFacingDirection = Vector2.zero;
            agent.ResetPath();
            State = UnitState.Idle;
            return;
        }

        // Mientras se mueve, girar hacia la dirección del movimiento
        FlipTowardVelocity();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ORDER INDICATORS (visualización de órdenes)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Mostrar indicador visual de movimiento.</summary>
    public void ShowMoveIndicator(Vector2 destination, Vector2 facingDirection, bool preview = false)
    {
        var indicator = GetComponent<OrderIndicator>();
        if (indicator != null)
            indicator.ShowMove(destination, facingDirection, preview);
    }

    /// <summary>Mostrar indicador visual de ataque.</summary>
    public void ShowAttackIndicator()
    {
        var indicator = GetComponent<OrderIndicator>();
        if (indicator != null && CurrentTarget != null)
            indicator.ShowAttack(CurrentTarget);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  UTILITIES
    // ─────────────────────────────────────────────────────────────────────

    protected override void RotateTowards(Vector2 direction)
    {
        base.RotateTowards(direction);

        // Mantener el indicador de selección siempre horizontal
        if (selectionIndicator != null)
            selectionIndicator.transform.rotation = Quaternion.identity;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  DEBUG GIZMOS
    // ─────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        if (!showDebugGizmos || !Application.isPlaying)
            return;

        // Visualizar facing deseado
        if (desiredFacingDirection != Vector2.zero)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position,
                transform.position + (Vector3)(desiredFacingDirection * 0.5f));
            Gizmos.DrawWireSphere((Vector3)((Vector2)transform.position + desiredFacingDirection * 0.5f), 0.1f);
        }
    }
#endif
}

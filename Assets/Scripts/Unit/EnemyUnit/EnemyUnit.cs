using UnityEngine;

/// <summary>
/// Unidad enemiga con IA básica:
///   · Desde Idle busca la unidad jugador más cercana dentro de detectionRange
///   · La persigue y ataca usando el sistema de combate de Unit.cs
///   · Re-evalúa el objetivo si muere o sale de rango
/// </summary>
public class EnemyUnit : Unit
{
    [Header("IA Enemiga")]
    [Tooltip("Radio dentro del cual el enemigo detecta unidades del jugador")]
    public float detectionRange = 5f;

    [Tooltip("Cada cuántos segundos re-evalúa si hay un objetivo mejor")]
    public float retargetInterval = 1.5f;

    private float _retargetTimer = 0f;

    // ─────────────────────────────────────────────────────────────────────────
    //  INIT
    // ─────────────────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        if (spriteRenderer != null)
            spriteRenderer.color = Color.red;

        side = UnitSide.Enemy;

        base.Awake(); // ahora lee Color.red como baseColor
    }
    // ─────────────────────────────────────────────────────────────────────────
    //  IDLE — buscar objetivo
    // ─────────────────────────────────────────────────────────────────────────

    protected override void UpdateIdle()
    {
        _retargetTimer -= Time.deltaTime;
        if (_retargetTimer > 0f) return;

        _retargetTimer = retargetInterval;

        Unit nearest = FindNearestPlayerUnit();
        if (nearest != null)
            OrderAttack(nearest);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ATTACKING — re-evaluar si el objetivo muere
    // ─────────────────────────────────────────────────────────────────────────

    public override void OnStateChanged(UnitState previous, UnitState next)
    {
        // Al volver a Idle desde Attacking, forzar retarget inmediato
        if (previous == UnitState.Attacking && next == UnitState.Idle)
            _retargetTimer = 0f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  BÚSQUEDA DE OBJETIVO
    // ─────────────────────────────────────────────────────────────────────────

    Unit FindNearestPlayerUnit()
    {
        Unit nearest = null;
        float minDist = detectionRange;

        // FindObjectsByType es caro — en el juego final sustituir por
        // un registro centralizado en CombatManager
        foreach (var unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            if (unit.side != UnitSide.Player || unit.IsDead) continue;

            float dist = Vector2.Distance(transform.position, unit.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = unit;
            }
        }

        return nearest;
    }

    protected override void Die()
    {
        base.Die();
        if (CombatManager.Instance != null)
            CombatManager.Instance.CheckVictoryCondition();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GIZMO — visualizar rango de detección en editor
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
#endif
}
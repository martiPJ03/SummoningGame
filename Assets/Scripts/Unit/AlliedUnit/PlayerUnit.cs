using UnityEngine;

/// <summary>
/// Versión de PlayerUnit que maneja 8 direcciones mediante Animator 
/// en lugar de rotación física del Transform.
/// </summary>
public class PlayerUnit : AlliedUnit
{
    [Header("8-Way Movement")]
    public Animator animator;

    // Nombres de los parámetros en tu Animator Controller
    private static readonly int dirX = Animator.StringToHash("dirX");
    private static readonly int dirY = Animator.StringToHash("dirY");
    private static readonly int isMoving = Animator.StringToHash("isMoving");

    protected override void Awake()
    {
        base.Awake();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    /// <summary>
    /// Sobrescribimos la rotación para que el Transform NO rote,
    /// pero el Animator sepa hacia dónde mirar.
    /// </summary>
    protected override void RotateTowards(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.01f) return;

        transform.rotation = Quaternion.identity;

        if (animator != null)
        {
            animator.SetFloat(dirX, direction.x);
            animator.SetFloat(dirY, direction.y);
        }
    }

    protected override void Update()
    {
        base.Update();

        // Actualizar estado de animación de movimiento
        if (animator != null)
        {
            bool moving = State == UnitState.Moving && agent.velocity.sqrMagnitude > 0.1f;
            //animator.SetBool(IsMoving, moving);
        }
    }

    protected override void Die()
    {
        base.Die();
        if (CombatManager.Instance != null)
            CombatManager.Instance.OnPlayerUnitDied(this);
    }
}
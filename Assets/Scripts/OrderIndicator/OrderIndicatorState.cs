using UnityEngine;

/// <summary>
/// Contenidor de l'estat compartit de l'indicador d'ordres.
/// No té lògica pròpia: és el "model" que llegeixen els altres subcomponents.
/// 
/// SETUP: afegir al mateix GameObject que OrderIndicator.
/// </summary>
public class OrderIndicatorState : MonoBehaviour
{
    public enum OrderType { None, Move, Attack }

    // ── Estat de l'ordre actual ───────────────────────────────────────────────
    public OrderType CurrentOrder { get; private set; } = OrderType.None;
    public Vector3 Destination { get; private set; }
    public Unit AttackTarget { get; private set; }

    // ── Facing ────────────────────────────────────────────────────────────────
    public Vector2 FacingDir { get; private set; } = Vector2.up;
    public bool FacingLocked { get; private set; } = false;

    // ── Path corners capturats durant la pausa ────────────────────────────────
    public Vector3[] StoredCorners { get; private set; }

    // ── Visibilitat ───────────────────────────────────────────────────────────
    public bool IsActive { get; private set; } = false;

    // ─────────────────────────────────────────────────────────────────────────
    //  ESCRIPTURA (només OrderIndicator crida aquests mètodes)
    // ─────────────────────────────────────────────────────────────────────────

    public void SetMove(Vector3 destination, Vector2 facingDir)
    {
        CurrentOrder = OrderType.Move;
        Destination = destination;
        AttackTarget = null;
        FacingLocked = facingDir.sqrMagnitude > 0.001f;
        FacingDir = FacingLocked ? facingDir.normalized : Vector2.up;
        StoredCorners = null;
        IsActive = true;
    }

    public void SetAttack(Unit target)
    {
        CurrentOrder = OrderType.Attack;
        AttackTarget = target;
        Destination = target != null ? target.transform.position : transform.position;
        FacingLocked = false;
        FacingDir = Vector2.up;
        StoredCorners = null;
        IsActive = true;
    }

    public void SetStoredCorners(Vector3[] corners)
    {
        StoredCorners = corners;
    }

    public void Clear()
    {
        CurrentOrder = OrderType.None;
        AttackTarget = null;
        StoredCorners = null;
        IsActive = false;
    }
}
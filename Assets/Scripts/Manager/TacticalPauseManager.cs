using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gestiona la pausa tàctica del joc:
///   · Congela l'escena (Time.timeScale = 0) però permet donar ordres
///   · Les ordres es posen en cua i s'executen en reprendre el joc
///   · Tecles: Space o P per toggle pausa/play
///
/// SETUP:
///   Afegir aquest component a un GameObject persistent (GameManager, etc.)
///   Assignar la referència des de TacticalPauseUI.
/// </summary>
public class TacticalPauseManager : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static TacticalPauseManager Instance { get; private set; }

    // ── Estat ────────────────────────────────────────────────────────────────
    public bool IsPaused { get; private set; } = false;

    // ── Cua d'ordres durant la pausa ─────────────────────────────────────────
    private readonly struct QueuedOrder
    {
        public enum OrderKind { Move, Attack, Stop }

        public readonly Unit Unit;
        public readonly OrderKind Kind;
        public readonly Vector3 Destination;
        public readonly Vector2 FacingDir;
        public readonly Unit AttackTarget;

        public QueuedOrder(Unit unit, Vector3 dest, Vector2 facing)
        {
            Unit = unit; Kind = OrderKind.Move;
            Destination = dest; FacingDir = facing; AttackTarget = null;
        }

        public QueuedOrder(Unit unit, Unit attackTarget)
        {
            Unit = unit; Kind = OrderKind.Attack;
            Destination = default; FacingDir = default; AttackTarget = attackTarget;
        }

    }

    private readonly Queue<QueuedOrder> _pendingOrders = new Queue<QueuedOrder>();

    // ── Eventos ───────────────────────────────────────────────────────────────
    public event Action OnPaused;
    public event Action OnResumed;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        // Toggle amb Space o P
        bool spacePressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        bool pPressed = Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame;

        if (spacePressed || pPressed)
            Toggle();
    }

    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;
        Time.timeScale = 0f;
        OnPaused?.Invoke();
        Debug.Log("[TacticalPause] PAUSA activada");
    }

    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;
        Time.timeScale = 1f;
        FlushPendingOrders();
        OnResumed?.Invoke();
        Debug.Log("[TacticalPause] JOC reprès");
    }

    public void Toggle()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CUA D'ORDRES  (cridada des de SelectionManager)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Afegir una ordre de moviment a la cua si estem en pausa,
    /// o executar-la directament si no ho estem.
    /// </summary>
    public void IssueMoveOrder(Unit unit, Vector3 destination, Vector2 facingDir = default)
    {
        if (unit == null || unit.IsDead) return;

        if (IsPaused)
        {
            _pendingOrders.Enqueue(new QueuedOrder(unit, destination, facingDir));
            // Mostra l'indicador visual immediatament (sense executar el NavMesh)
            var indicator = unit.GetComponent<OrderIndicator>();
            indicator?.ShowMove(destination, facingDir, preview: true);
            Debug.Log($"[TacticalPause] Ordre en cua → {unit.name} mou a {destination}");
        }
        else
        {
            unit.OrderMoveTo(destination, facingDir);
        }
    }

    /// <summary>
    /// Afegir una ordre d'atac a la cua si estem en pausa,
    /// o executar-la directament si no ho estem.
    /// </summary>
    public void IssueAttackOrder(Unit unit, Unit target)
    {
        if (unit == null || unit.IsDead || target == null || target.IsDead) return;

        if (IsPaused)
        {
            _pendingOrders.Enqueue(new QueuedOrder(unit, target));
            var indicator = unit.GetComponent<OrderIndicator>();
            indicator?.ShowAttack(target);
            Debug.Log($"[TacticalPause] Ordre en cua → {unit.name} ataca {target.name}");
        }
        else
        {
            unit.OrderAttack(target);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EXECUCIÓ DE LA CUA
    // ─────────────────────────────────────────────────────────────────────────

    void FlushPendingOrders()
    {
        while (_pendingOrders.Count > 0)
        {
            var order = _pendingOrders.Dequeue();

            if (order.Unit == null || order.Unit.IsDead) continue;

            switch (order.Kind)
            {
                case QueuedOrder.OrderKind.Move:
                    order.Unit.OrderMoveTo(order.Destination, order.FacingDir);
                    break;

                case QueuedOrder.OrderKind.Attack:
                    if (order.AttackTarget != null && !order.AttackTarget.IsDead)
                        order.Unit.OrderAttack(order.AttackTarget);
                    break;
            }
        }
        Debug.Log("[TacticalPause] Totes les ordres en cua executades");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CLEANUP
    // ─────────────────────────────────────────────────────────────────────────

    void OnDestroy()
    {
        // Assegurar que Time.timeScale es restaura si destruïm el manager
        if (IsPaused)
            Time.timeScale = 1f;
    }
}

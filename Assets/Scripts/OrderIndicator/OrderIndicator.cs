using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Coordinador de l'indicador visual d'ordres.
/// NO conté lògica pròpia: delega en els quatre subcomponents especialitzats:
///
///   · OrderIndicatorState   → estat compartit (model)
///   · OrderPathLine         → renderitzat de la línia del camí
///   · OrderIcon             → posició i rotació de l'icona de destí
///   · OrderIndicatorFader   → detecció d'arribada i fade-out
///
/// SETUP:
///   Afegir aquest component a la unitat. Els altres quatre components
///   s'afegeixen automàticament gràcies a RequireComponent.
///   Assignar moveSprite i attackSprite des de l'Inspector d'OrderIcon.
/// </summary>
[RequireComponent(typeof(Unit))]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(OrderIndicatorState))]
[RequireComponent(typeof(OrderPathLine))]
[RequireComponent(typeof(OrderIcon))]
[RequireComponent(typeof(OrderIndicatorFader))]
public class OrderIndicator : MonoBehaviour
{
    // Colors centralitzats aquí perquè tots els subcomponents els comparteixen
    static readonly Color ColorMove = new Color(0.25f, 0.95f, 0.35f, 1f);
    static readonly Color ColorAttack = new Color(0.95f, 0.25f, 0.25f, 1f);

    private OrderIndicatorState _state;
    private OrderPathLine _pathLine;
    private OrderIcon _icon;
    private OrderIndicatorFader _fader;
    private NavMeshAgent _agent;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _state = GetComponent<OrderIndicatorState>();
        _pathLine = GetComponent<OrderPathLine>();
        _icon = GetComponent<OrderIcon>();
        _fader = GetComponent<OrderIndicatorFader>();
        _agent = GetComponent<NavMeshAgent>();

        // Quan el fade acaba, netejem tot l'estat
        _fader.OnFadeComplete += HideAll;
    }

    void LateUpdate()
    {
        if (!_state.IsActive) return;

        _pathLine.Refresh();
        _icon.Refresh();

        bool frozen = TacticalPauseManager.Instance != null
                   && TacticalPauseManager.Instance.IsPaused;

        if (!frozen)
            _fader.CheckArrival();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    public void ShowMove(Vector3 destination, Vector2 facingDir, bool preview = false)
    {
        _fader.CancelFade();
        _state.SetMove(destination, facingDir);
        _state.SetStoredCorners(CalculatePathCorners(destination));

        bool applyRotation = preview || _state.FacingLocked;
        _icon.ShowMove(destination, _state.FacingDir, applyRotation);

        ApplyColor(ColorMove);

        if (preview)
        {
            _pathLine.SetLineVisible(false);
            _icon.SetIconVisible(true);
        }
        else
        {
            SetAllVisible(true);
        }
    }

    public void ShowAttack(Unit target)
    {
        _fader.CancelFade();
        _state.SetAttack(target);
        _state.SetStoredCorners(CalculatePathCorners(_state.Destination));

        _icon.ShowAttack(_state.Destination);

        ApplyColor(ColorAttack);
        SetAllVisible(true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS PRIVATS
    // ─────────────────────────────────────────────────────────────────────────

    void HideAll()
    {
        _state.Clear();
        _pathLine.SetLineVisible(false);
        _icon.SetIconVisible(false);
    }

    void ApplyColor(Color color)
    {
        _pathLine.ApplyColor(color);
        _icon.ApplyColor(color);
    }

    void SetAllVisible(bool visible)
    {
        _pathLine.SetLineVisible(visible);
        _icon.SetIconVisible(visible);
    }

    Vector3[] CalculatePathCorners(Vector3 destination)
    {
        if (_agent == null) return null;
        var path = new NavMeshPath();
        _agent.CalculatePath(destination, path);
        return path.corners;
    }
}
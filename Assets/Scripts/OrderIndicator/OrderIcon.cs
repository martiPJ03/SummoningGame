using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Responsabilitat ÚNICA: posicionar i rotar el sprite d'icona de destí.
///   · Move: segueix l'últim corner del path i aplica el facing
///   · Attack: segueix la posició del target amb rotació neutra
///
/// SETUP: afegir al mateix GameObject que OrderIndicator.
/// </summary>
[RequireComponent(typeof(OrderIndicatorState))]
public class OrderIcon : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite moveSprite;
    public Sprite attackSprite;

    [Header("Escala")]
    public float iconScale = 0.5f;

    private SpriteRenderer _renderer;
    private NavMeshAgent _agent;
    private OrderIndicatorState _state;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _state = GetComponent<OrderIndicatorState>();
        _agent = GetComponent<NavMeshAgent>();
        BuildSpriteRenderer();
        SetIconVisible(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Configura el sprite i mostra l'icona per a una ordre de moviment.</summary>
    public void ShowMove(Vector3 destination, Vector2 facingDir, bool applyRotation)
    {
        _renderer.sprite = moveSprite;
        _renderer.transform.position = new Vector3(destination.x, destination.y, -0.1f);

        if (applyRotation)
            ApplyFacingRotation(facingDir);

        SetIconVisible(true);
    }

    /// <summary>Configura el sprite i mostra l'icona per a una ordre d'atac.</summary>
    public void ShowAttack(Vector3 targetPosition)
    {
        _renderer.sprite = attackSprite;
        _renderer.transform.position = new Vector3(targetPosition.x, targetPosition.y, -0.1f);
        _renderer.transform.rotation = Quaternion.identity;

        SetIconVisible(true);
    }

    /// <summary>Actualitza posició i rotació cada frame.</summary>
    public void Refresh()
    {
        if (!_state.IsActive) { SetIconVisible(false); return; }

        switch (_state.CurrentOrder)
        {
            case OrderIndicatorState.OrderType.Move: RefreshMove(); break;
            case OrderIndicatorState.OrderType.Attack: RefreshAttack(); break;
        }
    }

    public void ApplyColor(Color color)
    {
        if (_renderer != null) _renderer.color = color;
    }

    public void ApplyAlpha(float alpha)
    {
        if (_renderer == null) return;
        var c = _renderer.color;
        c.a = alpha;
        _renderer.color = c;
    }

    public void SetIconVisible(bool visible)
    {
        if (_renderer != null) _renderer.enabled = visible;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  REFRESH PER FRAME
    // ─────────────────────────────────────────────────────────────────────────

    void RefreshMove()
    {
        // Si estamos siguiendo un multi-waypoint path, mostrar la icona solo al final
        if (_state.PathPoints != null && _state.PathPoints.Count > 0)
        {
            RefreshMultipleWaypointMove();
            return;
        } else
        {
            RefreshNormalMove();
        }
    }

    private void RefreshMultipleWaypointMove()
    {
        Vector2 lastWaypoint = _state.PathPoints[_state.PathPoints.Count - 1];
        _renderer.transform.position = new Vector3(lastWaypoint.x, lastWaypoint.y, -0.1f);
        // Inferir facing del darrer segment del path
        Vector2 facing = _state.FacingDir;
        if (_state.PathPoints.Count >= 2)
        {
            Vector2 lastSeg = _state.PathPoints[_state.PathPoints.Count - 1] - _state.PathPoints[_state.PathPoints.Count - 2];
            facing = lastSeg.normalized;
        }
        ApplyFacingRotation(facing);
    }

    private void RefreshNormalMove()
    {
        if (_state.FacingLocked)
        {
            _renderer.transform.position = new Vector3(_state.Destination.x, _state.Destination.y, -0.1f);
            ApplyFacingRotation(_state.FacingDir);
            return;
        }

        Vector3[] corners = ResolveCorners();
        if (corners == null) return;

        Vector3 endPos = corners[corners.Length - 1];
        _renderer.transform.position = new Vector3(endPos.x, endPos.y, -0.1f);

        // Si el facing no ve del drag, l'inferim del darrer segment del path
        Vector2 facing = _state.FacingDir;
        if (corners.Length >= 2)
        {
            Vector3 lastSeg = corners[corners.Length - 1] - corners[corners.Length - 2];
            facing = new Vector2(lastSeg.x, lastSeg.y).normalized;
        }

        ApplyFacingRotation(facing);
    }

    void RefreshAttack()
    {
        if (_state.AttackTarget == null) return;

        var pos = _state.AttackTarget.transform.position;
        _renderer.transform.position = new Vector3(pos.x, pos.y, -0.1f);
        _renderer.transform.rotation = Quaternion.identity;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UTILITATS
    // ─────────────────────────────────────────────────────────────────────────

    Vector3[] ResolveCorners()
    {
        if (_state.StoredCorners != null && _state.StoredCorners.Length >= 2)
            return _state.StoredCorners;

        if (_agent != null && _agent.hasPath && _agent.path.corners.Length >= 2)
            return _agent.path.corners;

        return null;
    }

    void ApplyFacingRotation(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _renderer.transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SETUP
    // ─────────────────────────────────────────────────────────────────────────

    void BuildSpriteRenderer()
    {
        var go = new GameObject("OrderIcon");
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * iconScale;

        _renderer = go.AddComponent<SpriteRenderer>();
        _renderer.sortingOrder = 11;
    }

    void OnDestroy()
    {
        if (_renderer != null) Destroy(_renderer.gameObject);
    }
}
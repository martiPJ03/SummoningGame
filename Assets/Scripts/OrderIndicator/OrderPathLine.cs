using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Responsabilitat ÚNICA: renderitzar la línia del camí de la unitat.
///   · En temps real: usa els corners del NavMeshAgent
///   · En pausa: usa els corners capturats a OrderIndicatorState
///   · Si no hi ha path calculat: dibuixa una línia recta provisional
///
/// SETUP: afegir al mateix GameObject que OrderIndicator.
/// </summary>
[RequireComponent(typeof(OrderIndicatorState))]
public class OrderPathLine : MonoBehaviour
{
    [Header("Configuració de línia")]
    public float lineWidth = 0.06f;
    public int pathResolution = 60;

    private LineRenderer _pathLine;
    private NavMeshAgent _agent;
    private OrderIndicatorState _state;
    private Unit _unit;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _state = GetComponent<OrderIndicatorState>();
        _agent = GetComponent<NavMeshAgent>();
        _unit = GetComponent<Unit>();
        BuildLineRenderer();
        SetLineVisible(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    public void Refresh()
    {
        if (!_state.IsActive) { SetLineVisible(false); return; }

        SetLineVisible(true);

        if (_state.PathPoints != null && _state.PathPoints.Count > 0)
        {
            int fromIndex = (_unit != null && _unit.IsFollowingPath)
                ? _unit.CurrentWaypointIndex
                : _state.PathPoints.Count; // si ya terminó, no renderizar nada

            RenderMultiWaypointPath(_state.PathPoints, fromIndex);
            return;
        }

        bool frozen = TacticalPauseManager.Instance != null
                   && TacticalPauseManager.Instance.IsPaused;

        if (!frozen && _agent != null && _agent.hasPath)
        {
            _state.SetStoredCorners(_agent.path.corners);
            RenderCorners(_agent.path.corners);
        }
        else if (_state.StoredCorners != null && _state.StoredCorners.Length > 1)
        {
            RenderCorners(_state.StoredCorners);
        }
        else
        {
            RenderStraightLine();
        }
    }

    public void ApplyColor(Color color)
    {
        if (_pathLine == null) return;
        _pathLine.startColor = color;
        _pathLine.endColor = new Color(color.r, color.g, color.b, color.a * 0.4f);
    }

    public void ApplyAlpha(float alpha)
    {
        if (_pathLine == null) return;
        var c = _pathLine.startColor;
        c.a = alpha;
        _pathLine.startColor = c;
        c = _pathLine.endColor;
        c.a = alpha * 0.4f;
        _pathLine.endColor = c;
    }

    public void SetLineVisible(bool visible)
    {
        if (_pathLine == null) return;
        _pathLine.enabled = visible;
        if (!visible) _pathLine.positionCount = 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  RENDERITZAT
    // ─────────────────────────────────────────────────────────────────────────

    void RenderMultiWaypointPath(List<Vector2> waypoints, int fromIndex)
    {
        int remaining = waypoints.Count - fromIndex;
        if (remaining <= 0) { _pathLine.positionCount = 0; return; }

        // unit position + remaining waypoints
        var positions = new Vector3[remaining + 1];
        positions[0] = new Vector3(transform.position.x, transform.position.y, -0.1f);

        for (int i = 0; i < remaining; i++)
        {
            var wp = waypoints[fromIndex + i];
            positions[i + 1] = new Vector3(wp.x, wp.y, -0.1f);
        }

        _pathLine.positionCount = positions.Length;
        for (int i = 0; i < positions.Length; i++)
            _pathLine.SetPosition(i, positions[i]);
    }

    void RenderCorners(Vector3[] corners)
    {
        if (corners == null || corners.Length < 2) { _pathLine.positionCount = 0; return; }

        int startIndex = 0;

        int count = Mathf.Min(corners.Length - startIndex, pathResolution);
        _pathLine.positionCount = count;

        for (int i = 0; i < count; i++)
        {
            var pos = corners[startIndex + i];
            pos.z = -0.1f;
            _pathLine.SetPosition(i, pos);
        }
    }

    void RenderStraightLine()
    {
        Vector3 effectiveDest = (_state.CurrentOrder == OrderIndicatorState.OrderType.Attack
                              && _state.AttackTarget != null)
            ? _state.AttackTarget.transform.position
            : _state.Destination;

        _pathLine.positionCount = 2;
        _pathLine.SetPosition(0, new Vector3(transform.position.x, transform.position.y, -0.1f));
        _pathLine.SetPosition(1, new Vector3(effectiveDest.x, effectiveDest.y, -0.1f));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SETUP
    // ─────────────────────────────────────────────────────────────────────────

    void BuildLineRenderer()
    {
        var go = new GameObject("PathLine");
        go.transform.SetParent(transform, false);

        _pathLine = go.AddComponent<LineRenderer>();
        _pathLine.useWorldSpace = true;
        _pathLine.startWidth = lineWidth;
        _pathLine.endWidth = lineWidth;
        _pathLine.positionCount = 0;
        _pathLine.sortingOrder = 10;
        _pathLine.numCapVertices = 4;
        _pathLine.numCornerVertices = 4;
        _pathLine.material = new Material(Shader.Find("Sprites/Default"));
    }

    void OnDestroy()
    {
        if (_pathLine != null) Destroy(_pathLine.gameObject);
    }
}
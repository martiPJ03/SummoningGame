using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Indicador visual de órdenes: línea de path + sprite de icono.
/// Requiere que el GameObject tenga un componente Unit.
/// Asigna moveSprite y attackSprite desde el Inspector.
/// </summary>
[RequireComponent(typeof(Unit))]
public class OrderIndicator : MonoBehaviour
{
    // ── Configuración ─────────────────────────────────────────────────────────

    [Header("Línea de path")]
    public float lineWidth = 0.06f;
    public int pathResolution = 30;

    [Header("Sprites de iconos")]
    public Sprite moveSprite;
    public Sprite attackSprite;
    public float iconScale = 0.5f;

    [Header("Fade")]
    public float fadeDuration = 0.4f;
    public float fadeDelay = 0.15f;

    // ── Colores ───────────────────────────────────────────────────────────────

    static readonly Color ColorMove = new Color(0.25f, 0.95f, 0.35f, 1f);
    static readonly Color ColorAttack = new Color(0.95f, 0.25f, 0.25f, 1f);

    // ── Estado interno ────────────────────────────────────────────────────────

    public enum OrderType { None, Move, Attack }

    private OrderType _currentOrder = OrderType.None;
    private Vector3 _destination;
    private Unit _attackTarget;
    private Unit _unit;
    private NavMeshAgent _agent;

    private LineRenderer _pathLine;
    private SpriteRenderer _iconRenderer;

    private Coroutine _fadeRoutine;
    private float _alpha = 0f;
    private bool _active = false;
    private Color _baseColor = Color.white;

    private Vector2 _facingDir = Vector2.up;
    private bool _facingLocked = false;  // true cuando el facing viene del drag, no del path


    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _unit = GetComponent<Unit>();
        _agent = GetComponent<NavMeshAgent>();
        SetupVisuals();
        SetVisible(false);
    }

    void Update()
    {
        if (!_active) return;

        UpdatePathLine();
        UpdateIconPosition();
        bool frozen = TacticalPauseManager.Instance != null && TacticalPauseManager.Instance.IsPaused;
        if (!frozen)
            CheckArrival();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    public void ShowMove(Vector3 destination, Vector2 facingDir, bool preview = false)
    {
        StopFade();

        _currentOrder = OrderType.Move;
        _destination = destination;
        _attackTarget = null;
        _alpha = 1f;
        _active = true;

        // Determinar si el facing está bloqueado
        _facingLocked = facingDir.sqrMagnitude > 0.001f;
        _facingDir = _facingLocked ? facingDir.normalized : Vector2.up;

        if (_iconRenderer != null)
        {
            _iconRenderer.sprite = moveSprite;
            _iconRenderer.transform.position = new Vector3(destination.x, destination.y, -0.1f);

            // Diferencia clave entre modo normal y preview
            if (preview || _facingLocked)
            {
                ApplyFacingRotation(_facingDir);
            }
        }

        ApplyColor(ColorMove);
        SetVisible(true);
    }

    public void ShowAttack(Unit target)
    {
        StopFade();
        _currentOrder = OrderType.Attack;
        _attackTarget = target;
        _destination = target != null ? target.transform.position : transform.position;
        _alpha = 1f;
        _active = true;

        if (_iconRenderer != null)
        {
            _iconRenderer.sprite = attackSprite;
            _iconRenderer.transform.rotation = Quaternion.identity;
            _iconRenderer.transform.position = new Vector3(_destination.x, _destination.y, -0.1f);
        }

        ApplyColor(ColorAttack);
        SetVisible(true);
    }

    public void Hide()
    {
        StopFade();
        _active = false;
        _currentOrder = OrderType.None;
        SetVisible(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UPDATE PER FRAME
    // ─────────────────────────────────────────────────────────────────────────

    void UpdatePathLine()
    {
        if (_pathLine == null) return;

        // Destino efectivo según tipo de orden
        Vector3 effectiveDest = (_currentOrder == OrderType.Attack && _attackTarget != null)
            ? _attackTarget.transform.position
            : _destination;

        bool frozen = TacticalPauseManager.Instance != null && TacticalPauseManager.Instance.IsPaused;

        if (!frozen && _agent != null && _agent.hasPath && _agent.path.corners.Length > 1)
        {
            var corners = _agent.path.corners;
            int count = Mathf.Min(corners.Length, pathResolution);
            _pathLine.positionCount = count;

            for (int cornerIndex = 0; cornerIndex < count; cornerIndex++)
            {
                var cornerPos = corners[cornerIndex];
                cornerPos.z = -0.1f;
                _pathLine.SetPosition(cornerIndex, cornerPos);
            }
        }
        else
        {
            // Sin path calculado aún: línea recta provisional
            _pathLine.positionCount = 2;
            _pathLine.SetPosition(0, new Vector3(transform.position.x, transform.position.y, -0.1f));
            _pathLine.SetPosition(1, new Vector3(effectiveDest.x, effectiveDest.y, -0.1f));
        }
    }

    void UpdateIconPosition()
    {
        if (_currentOrder != OrderType.Move) return;
        if (_iconRenderer == null) return;

        if (_agent != null && _agent.hasPath && _agent.path.corners.Length >= 2)
        {
            var corners = _agent.path.corners;

            // Posición: siempre el último corner del path
            Vector3 endPos = corners[corners.Length - 1];
            _iconRenderer.transform.position = new Vector3(endPos.x, endPos.y, -0.1f);

            // Rotación: si el facing viene del drag, lo respetamos
            // Si no, usamos el último segmento del path
            if (!_facingLocked)
            {
                Vector3 lastSeg = corners[corners.Length - 1] - corners[corners.Length - 2];
                _facingDir = new Vector2(lastSeg.x, lastSeg.y).normalized;
            }

            ApplyFacingRotation(_facingDir);
        }
    }

    void ApplyFacingRotation(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _iconRenderer.transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    void CheckArrival()
    {
        if (_fadeRoutine != null) return;

        bool arrived = false;

        if (_currentOrder == OrderType.Move)
        {
            // Solo fade si el agente realmente llegó (no solo por estar en Idle al inicio)
            bool agentDone = _agent != null
                && !_agent.pathPending
                && _agent.remainingDistance <= _agent.stoppingDistance;

            float dist = Vector2.Distance(transform.position, _destination);
            arrived = agentDone || dist < 0.25f;
        }
        else if (_currentOrder == OrderType.Attack)
        {
            arrived = _unit.State == UnitState.Attacking
                   || _attackTarget == null
                   || _attackTarget.IsDead;
        }

        if (arrived)
            _fadeRoutine = StartCoroutine(FadeOut());
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  FADE
    // ─────────────────────────────────────────────────────────────────────────

    IEnumerator FadeOut()
    {
        yield return new WaitForSecondsRealtime(fadeDelay);

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            ApplyAlpha(_alpha);
            yield return null;
        }

        Hide();
        _fadeRoutine = null;
    }

    void StopFade()
    {
        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SETUP VISUAL
    // ─────────────────────────────────────────────────────────────────────────

    void SetupVisuals()
    {
        // 1. LineRenderer para el path
        var lineGo = new GameObject("PathLine");
        lineGo.transform.SetParent(transform, false);

        _pathLine = lineGo.AddComponent<LineRenderer>();
        _pathLine.useWorldSpace = true;
        _pathLine.startWidth = lineWidth;
        _pathLine.endWidth = lineWidth;
        _pathLine.positionCount = 0;
        _pathLine.sortingOrder = 10;
        _pathLine.numCapVertices = 4;
        _pathLine.numCornerVertices = 4;
        _pathLine.material = new Material(Shader.Find("Sprites/Default"));

        // 2. SpriteRenderer para el icono de destino
        var iconGo = new GameObject("OrderIcon");
        iconGo.transform.localScale = Vector3.one * iconScale;

        _iconRenderer = iconGo.AddComponent<SpriteRenderer>();
        _iconRenderer.sortingOrder = 11;
    }

    void OnDestroy()
    {
        if (_iconRenderer != null) Destroy(_iconRenderer.gameObject);
        if (_pathLine != null) Destroy(_pathLine.gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  COLOR Y ALPHA
    // ─────────────────────────────────────────────────────────────────────────

    void ApplyColor(Color color)
    {
        _baseColor = color;
        ApplyAlpha(1f);
    }

    void ApplyAlpha(float alpha)
    {
        Color c = new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);

        if (_pathLine != null)
        {
            _pathLine.startColor = c;
            _pathLine.endColor = new Color(c.r, c.g, c.b, c.a * 0.4f);
        }

        if (_iconRenderer != null)
            _iconRenderer.color = c;
    }

    void SetVisible(bool visible)
    {
        if (_pathLine != null) _pathLine.enabled = visible;
        if (_iconRenderer != null) _iconRenderer.enabled = visible;

        // Cuando ocultamos, limpiamos posiciones para no dejar geometría residual
        if (!visible && _pathLine != null)
            _pathLine.positionCount = 0;
    }
}
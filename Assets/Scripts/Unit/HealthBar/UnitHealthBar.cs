using UnityEngine;

[RequireComponent(typeof(Unit))]
public class UnitHealthBar : MonoBehaviour
{
    [Header("Mides (en unitats món)")]
    public float barWidth = 1.0f;
    public float barHeight = 0.12f;
    public float yOffset = 0.75f;

    [Header("Colors")]
    public Color allyColor = new Color(0.2f, 0.85f, 0.2f);
    public Color enemyColor = new Color(0.85f, 0.15f, 0.15f);
    public Color bgColor = new Color(0.08f, 0.08f, 0.08f);
    public Color borderColor = new Color(0f, 0f, 0f);

    [Header("Sorting")]
    public int sortingOrder = 20;

    // ── Refs ──────────────────────────────────────────────────────────────
    private Transform _barRoot;   // ← guardamos la ref para resetear rotación
    private SpriteRenderer _border;
    private SpriteRenderer _bg;
    private SpriteRenderer _fill;

    private Unit _unit;
    private float _currentFill = 1f;
    private float _targetFill = 1f;

    private const float FillLerpSpeed = 8f;

    // ─────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _unit = GetComponent<Unit>();
        BuildBar();

        _unit.onHealthChanged.AddListener(OnHealthChanged);
        _unit.onDeath.AddListener(_ =>
        {
            if (_barRoot != null) Destroy(_barRoot.gameObject);
        });
    }

    void LateUpdate()   // LateUpdate: la unitat ja ha rotat aquest frame
    {
        if (_barRoot == null) return;

        _barRoot.position = transform.position + new Vector3(0f, yOffset, -0.2f);

        _currentFill = Mathf.Lerp(_currentFill, _targetFill,
                                   Time.deltaTime * FillLerpSpeed);
        BarBuilder.UpdateFillScale(_fill, barWidth, barHeight, _currentFill);

        bool show = _targetFill < 0.999f;
        _border.enabled = show;
        _bg.enabled = show;
        _fill.enabled = show;
    }

    void OnDestroy()
    {
        if (_unit != null)
            _unit.onHealthChanged.RemoveListener(OnHealthChanged);

        if (_barRoot != null) Destroy(_barRoot.gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  EVENT
    // ─────────────────────────────────────────────────────────────────────

    void OnHealthChanged(float current, float max)
    {
        _targetFill = (max > 0f) ? Mathf.Clamp01(current / max) : 0f;
        ApplyColor();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BUILD  (jerarquia plana — sense container intermedi)
    // ─────────────────────────────────────────────────────────────────────

    void BuildBar()
    {
        BarBuilder.BuildBar(
            "HealthBar",
            barWidth,
            barHeight,
            0f,
            yOffset,
            -0.2f,
            borderColor,
            bgColor,
            sortingOrder,
            out _barRoot,
            out _border,
            out _bg,
            out _fill
        );
    }

    // ─────────────────────────────────────────────────────────────────────
    //  UTILS
    // ─────────────────────────────────────────────────────────────────────

    void ApplyColor()
    {
        if (_fill == null) return;
        _fill.color = (_unit.side == UnitSide.Player) ? allyColor : enemyColor;
    }
}
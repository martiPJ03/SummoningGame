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
        ApplyColor();

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
        UpdateFillScale();

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
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BUILD  (jerarquia plana — sense container intermedi)
    // ─────────────────────────────────────────────────────────────────────

    void BuildBar()
    {
        Sprite px = CreatePixelSprite();

        // Root: child de la unitat, posició fixa en Y
        var root = new GameObject("HealthBar");
        root.transform.SetParent(null);
        root.transform.localPosition = new Vector3(0f, yOffset, -0.2f);
        _barRoot = root.transform;

        float borderPad = barHeight * 0.3f;

        // Border — centrat, lleugerament més gran
        _border = MakeRenderer(root, "Border", px, borderColor, sortingOrder - 2);
        _border.transform.localScale = new Vector3(barWidth + borderPad,
                                                   barHeight + borderPad, 1f);
        _border.transform.localPosition = Vector3.zero;

        // Background — centrat, mida exacta de la barra
        _bg = MakeRenderer(root, "BG", px, bgColor, sortingOrder - 1);
        _bg.transform.localScale = new Vector3(barWidth, barHeight, 1f);
        _bg.transform.localPosition = Vector3.zero;

        // FIX 2: Fill — pivot al costat esquerre mitjançant posició
        // El sprite té pivot central (0.5, 0.5). Per simular pivot esquerre:
        //   posicionem el fill a (-barWidth/2 + fillWidth/2, 0)
        // Comencem amb fillWidth = barWidth (vida plena) i ho actualitzem a UpdateFillScale
        _fill = MakeRenderer(root, "Fill", px, Color.white, sortingOrder);
        _fill.transform.localScale = new Vector3(barWidth, barHeight, 1f);
        _fill.transform.localPosition = new Vector3(0f, 0f, -0.05f);
    }

    void UpdateFillScale()
    {
        if (_fill == null) return;

        float fillWidth = barWidth * _currentFill;

        // Escalar en X
        _fill.transform.localScale = new Vector3(fillWidth, barHeight, 1f);

        // Mantenir el pivot a l'esquerra:
        // centre del fill = -barWidth/2 + fillWidth/2
        _fill.transform.localPosition = new Vector3(
            -barWidth * 0.5f + fillWidth * 0.5f,
            0f,
            -0.05f
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

    static SpriteRenderer MakeRenderer(GameObject parent, string name,
                                       Sprite sprite, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    static Sprite CreatePixelSprite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }
}
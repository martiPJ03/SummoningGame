using UnityEngine;
using TMPro;

/// <summary>
/// Barra de maná a la UI del jugador (posició fixa, low center).
/// Mostra:
///   · Barra de fill animada (fill amount horitzontal)
///   · Text amb maná actual / màxim (opcional)
///   · Text amb flux net de maná per segon (opcional)
///   · Text amb nombre de summons actius (opcional)
///
/// Construeix la barra amb SpriteRenderers en mundo-space (posició fixa).
/// Funciona amb ManaManager (Singleton).
/// </summary>
public class ManaBarUI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    //  MIDES I POSICIÓ (Inspector)
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Mides (en unitats món)")]
    public float barWidth = 2.0f;
    public float barHeight = 0.15f;

    [Header("Posició fixa (mundo-space, low center)")]
    [Tooltip("Posició X de la barra")]
    public float barPositionX = 0f;

    [Tooltip("Posició Y de la barra (baixa part de la pantalla)")]
    public float barPositionY = -4f;

    [Header("Colors")]
    [Tooltip("Color quan el maná és alt")]
    public Color highManaColor = new Color(0.25f, 0.55f, 1.00f);   // blau

    [Tooltip("Color quan el maná és baix")]
    public Color lowManaColor = new Color(0.70f, 0.15f, 0.85f);    // porpra fosc

    [Tooltip("Color de fons de la barra")]
    public Color bgColor = new Color(0.08f, 0.08f, 0.08f);

    [Tooltip("Color del border")]
    public Color borderColor = new Color(0f, 0f, 0f);

    [Tooltip("Llindar per considerar maná 'baix' [0..1]")]
    [Range(0f, 0.5f)]
    public float lowManaThreshold = 0.25f;

    [Header("Sorting")]
    public int sortingOrder = 20;

    [Header("Textos (opcionals — TMPro)")]
    [Tooltip("Ex: '75 / 100'")]
    public TMP_Text manaValueText;

    [Tooltip("Ex: '+3.0/s' o '-1.2/s'")]
    public TMP_Text netFlowText;

    [Tooltip("Ex: '3 summons'")]
    public TMP_Text summonCountText;

    // ─────────────────────────────────────────────────────────────────────────
    //  ESTAT INTERN
    // ─────────────────────────────────────────────────────────────────────────

    private Transform _barRoot;
    private SpriteRenderer _border;
    private SpriteRenderer _bg;
    private SpriteRenderer _fill;
    private bool _subscribed = false;

    private float _currentFill = 1f;
    private float _targetFill = 1f;

    private const float FillLerpSpeed = 8f;

    // ─────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────

    void Awake()
    {
        BuildBar();
    }

    void OnEnable()
    {
        TrySubscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void LateUpdate()
    {
        if (_barRoot == null) return;

        // Posició fixa (no seguir cap unitat)
        _barRoot.position = new Vector3(barPositionX, barPositionY, -0.2f);

        // Subscripció diferida: ManaSystem pot inicialitzar-se després de la UI
        if (!_subscribed)
            TrySubscribe();

        // Animar el fill
        _currentFill = Mathf.Lerp(_currentFill, _targetFill,
                                   Time.unscaledDeltaTime * FillLerpSpeed);
        BarBuilder.UpdateFillScale(_fill, barWidth, barHeight, _currentFill);

        // Actualitzar textos
        RefreshNetFlowText();
        RefreshSummonCountText();

        _border.enabled = true;
        _bg.enabled = true;
        _fill.enabled = true;
    }

    void OnDestroy()
    {
        Unsubscribe();
        if (_barRoot != null)
            Destroy(_barRoot.gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SUBSCRIPCIÓ AL MANASYSTEM
    // ─────────────────────────────────────────────────────────────────────────

    void TrySubscribe()
    {
        if (_subscribed || ManaManager.Instance == null) return;

        ManaManager.Instance.OnManaChanged += OnManaChanged;
        _subscribed = true;

        // Sincronitzar immediatament amb l'estat actual
        OnManaChanged(ManaManager.Instance.CurrentMana, ManaManager.Instance.maxMana);
        _currentFill = _targetFill; // Evitar animació inicial
        BarBuilder.UpdateFillScale(_fill, barWidth, barHeight, _currentFill);
        ApplyFillColor(_currentFill);
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;

        if (ManaManager.Instance != null)
            ManaManager.Instance.OnManaChanged -= OnManaChanged;

        _subscribed = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CALLBACK
    // ─────────────────────────────────────────────────────────────────────────

    void OnManaChanged(float current, float max)
    {
        _targetFill = max > 0f ? Mathf.Clamp01(current / max) : 0f;

        if (manaValueText != null)
            manaValueText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";

        ApplyFillColor(_targetFill);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ANIMACIÓ
    // ─────────────────────────────────────────────────────────────────────────

    void ApplyFillColor(float fill)
    {
        if (_fill == null) return;

        // Interpolació de color: blau (ple) → porpra (baix)
        float t = fill <= lowManaThreshold
            ? 0f
            : Mathf.InverseLerp(lowManaThreshold, 1f, fill);

        _fill.color = Color.Lerp(lowManaColor, highManaColor, t);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TEXTOS DINÀMICS (actualitzats cada frame — poc cost)
    // ─────────────────────────────────────────────────────────────────────────

    void RefreshNetFlowText()
    {
        if (netFlowText == null || ManaManager.Instance == null) return;

        float net = ManaManager.Instance.NetManaPerSecond;
        netFlowText.text = net >= 0f
            ? $"<color=#88DDFF>+{net:F1}/s</color>"
            : $"<color=#FF6655>{net:F1}/s</color>";
    }

    void RefreshSummonCountText()
    {
        if (summonCountText == null || ManaManager.Instance == null) return;

        int count = ManaManager.Instance.ActiveSummonCount;
        summonCountText.text = count == 1
            ? "1 summon actiu"
            : $"{count} summons actius";
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BUILD  (jerarquia plana — sense container intermedi)
    // ─────────────────────────────────────────────────────────────────────

    void BuildBar()
    {
        BarBuilder.BuildBar(
            "ManaBar",
            barWidth,
            barHeight,
            barPositionX,
            barPositionY,
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
}
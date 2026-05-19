using UnityEngine;
using TMPro;
using UnityEngine.UI;

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
    [Header("Colors")]
    [Tooltip("Color quan el maná és alt")]
    public Color highManaColor = new Color(0.25f, 0.55f, 1.00f);   // blau

    [Tooltip("Color quan el maná és baix")]
    public Color lowManaColor = new Color(0.70f, 0.15f, 0.85f);    // porpra fosc

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
    
    [Tooltip("Posa el Fill'")]
    public Image manaBarFill;

    // ─────────────────────────────────────────────────────────────────────────
    //  ESTAT INTERN
    // ─────────────────────────────────────────────────────────────────────────

    private bool _subscribed = false;

    private float _currentFill = 1f;
    private float _targetFill = 1f;

    private const float FillLerpSpeed = 8f;

    // ─────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────

    void Awake()
    {
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
        // Subscripció diferida: ManaSystem pot inicialitzar-se després de la UI
        if (!_subscribed)
            TrySubscribe();

        // Actualitzar textos
        RefreshNetFlowText();
        RefreshSummonCountText();
    }

    void OnDestroy()
    {
        Unsubscribe();
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
        RefreshManaBar(current, max);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ANIMACIÓ
    // ─────────────────────────────────────────────────────────────────────────

    void ApplyFillColor(float fill)
    {
        if (manaBarFill == null) return;

        // Interpolació de color: blau (ple) → porpra (baix)
        float t = fill <= lowManaThreshold
            ? 0f
            : Mathf.InverseLerp(lowManaThreshold, 1f, fill);

        manaBarFill.color = Color.Lerp(lowManaColor, highManaColor, t);
    }

    private void RefreshManaBar(float current, float max)
    {
        if (manaBarFill == null || max <= 0f) return;

        float pct = Mathf.Clamp01(current / max);
        manaBarFill.fillAmount = pct;
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
}
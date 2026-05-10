using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sistema central de Maná del jugador.
/// Responsabilitats:
///   · Emmagatzemar el maná actual, màxim i mínim
///   · Regenerar maná passivament cada segon
///   · Drenar maná dels summons actius (UnitManaCost registrats)
///   · Exposar esdeveniments per sincronitzar la UI i altres sistemes
///
/// SETUP:
///   Afegir aquest component a un GameObject persistent (GameManager, etc.)
///   No cal res més: els UnitManaCost es registren/desregistren sols.
/// </summary>
public class ManaManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static ManaManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONFIGURACIÓ (Inspector)
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Valors base")]
    [Tooltip("Maná màxim del jugador")]
    public float maxMana = 100f;

    [Tooltip("Maná mínim (mai baixa d'aquí)")]
    public float minMana = 0f;

    [Tooltip("Maná inicial al arrencar (% del màxim)")]
    [Range(0f, 100f)]
    public float startManaPercent = 100f;

    [Header("Regeneració")]
    [Tooltip("Maná recuperat per segon de forma passiva")]
    public float regenPerSecond = 5f;

    [Header("Debug")]
    public bool logChanges = false;

    // ─────────────────────────────────────────────────────────────────────────
    //  ESTAT PÚBLIC (read-only)
    // ─────────────────────────────────────────────────────────────────────────


    /// <summary>Maná actual del jugador.</summary>
    public float CurrentMana { get; private set; }

    /// <summary>Percentatge de maná [0..1].</summary>
    public float ManaRatio => maxMana > 0f ? CurrentMana / maxMana : 0f;

    /// <summary>Drenatge total dels summons actius (maná/s).</summary>
    public float TotalDrainPerSecond
    {
        get
        {
            float total = 0f;
            foreach (var c in _consumers) total += c.MaintenanceCostPerSecond;
            return total;
        }
    }

    /// <summary>Flux net de maná per segon (positiu = guany, negatiu = pèrdua).</summary>
    public float NetManaPerSecond => regenPerSecond - TotalDrainPerSecond;

    /// <summary>Nombre de summons actius que consumeixen maná.</summary>
    public int ActiveSummonCount => _consumers.Count;

    // ─────────────────────────────────────────────────────────────────────────
    //  EVENTS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Disparat cada cop que el maná canvia. (actual, màxim)</summary>
    public event Action<float, float> OnManaChanged;

    /// <summary>Disparat quan el maná arriba al mínim.</summary>
    public event Action OnManaDepleted;

    /// <summary>Disparat quan el maná arriba al màxim.</summary>
    public event Action OnManaFull;

    /// <summary>Disparat quan un nou summon es registra o desregistra.</summary>
    public event Action OnConsumersChanged;

    // ─────────────────────────────────────────────────────────────────────────
    //  ESTAT INTERN
    // ─────────────────────────────────────────────────────────────────────────

    private readonly List<UnitManaCost> _consumers = new List<UnitManaCost>();
    private float _previousMana;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        CurrentMana = maxMana * (startManaPercent / 100f);
        _previousMana = CurrentMana;
    }

    void Update()
    {
        // Flux net = regeneració passiva − drenatge total dels summons
        float netDelta = NetManaPerSecond * Time.deltaTime;
        ApplyManaDelta(netDelta);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Intenta gastar <paramref name="amount"/> de maná.
    /// Retorna <c>true</c> si hi havia prou maná i s'ha descomptat.
    /// </summary>
    public bool TrySpendMana(float amount)
    {
        if (amount <= 0f) return true;
        if (CurrentMana < amount) return false;

        SetMana(CurrentMana - amount);
        return true;
    }

    /// <summary>Afegeix maná (pocions, recompenses, etc.).</summary>
    public void AddMana(float amount)
    {
        if (amount <= 0f) return;
        SetMana(CurrentMana + amount);
    }

    /// <summary>
    /// Comprova si hi ha prou maná per invocar sense descomptar-lo.
    /// Útil per desactivar botons de la UI.
    /// </summary>
    public bool CanAfford(float cost) => CurrentMana >= cost;

    // ─────────────────────────────────────────────────────────────────────────
    //  REGISTRE DE CONSUMIDORS (cridat per UnitManaCost)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Registra un summon actiu perquè dreni maná cada segon.</summary>
    public void RegisterConsumer(UnitManaCost consumer)
    {
        if (consumer == null || _consumers.Contains(consumer)) return;
        _consumers.Add(consumer);
        OnConsumersChanged?.Invoke();

        if (logChanges)
            Debug.Log($"[ManaSystem] Summon registrat: {consumer.name} " +
                      $"({consumer.MaintenanceCostPerSecond} maná/s). " +
                      $"Total drain: {TotalDrainPerSecond:F1}/s");
    }

    /// <summary>Desregistra un summon (mort, reinvocació, etc.).</summary>
    public void UnregisterConsumer(UnitManaCost consumer)
    {
        if (consumer == null || !_consumers.Contains(consumer)) return;
        _consumers.Remove(consumer);
        OnConsumersChanged?.Invoke();

        if (logChanges)
            Debug.Log($"[ManaSystem] Summon desregistrat: {consumer.name}. " +
                      $"Total drain: {TotalDrainPerSecond:F1}/s");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  LÒGICA INTERNA
    // ─────────────────────────────────────────────────────────────────────────

    void ApplyManaDelta(float delta)
    {
        SetMana(CurrentMana + delta);
    }

    void SetMana(float value)
    {
        float clamped = Mathf.Clamp(value, minMana, maxMana);

        // Evitar disparar esdeveniments innecessaris cada frame quan és 0 o màxim
        bool changed = Mathf.Abs(clamped - _previousMana) > 0.001f;
        if (!changed) return;

        bool wasAtMin = _previousMana <= minMana;
        bool wasAtMax = _previousMana >= maxMana;

        CurrentMana = clamped;

        OnManaChanged?.Invoke(CurrentMana, maxMana);

        if (!wasAtMin && CurrentMana <= minMana) OnManaDepleted?.Invoke();
        if (!wasAtMax && CurrentMana >= maxMana) OnManaFull?.Invoke();

        if (logChanges)
            Debug.Log($"[ManaSystem] {CurrentMana:F1}/{maxMana:F1}  " +
                      $"(net: {NetManaPerSecond:+0.0;-0.0}/s)");

        _previousMana = CurrentMana;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CLEANUP
    // ─────────────────────────────────────────────────────────────────────────

    void OnDestroy()
    {
        _consumers.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EDITOR UTILS
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    [Header("— Editor Debug —")]
    [SerializeField] private float _debugAmount = 10f;

    [ContextMenu("Afegir maná (debug)")]
    void DbgAddMana() => AddMana(_debugAmount);

    [ContextMenu("Gastar maná (debug)")]
    void DbgSpendMana() => TrySpendMana(_debugAmount);

    [ContextMenu("Buidar maná (debug)")]
    void DbgEmptyMana() => SetMana(minMana);

    [ContextMenu("Omplir maná (debug)")]
    void DbgFillMana() => SetMana(maxMana);
#endif
}
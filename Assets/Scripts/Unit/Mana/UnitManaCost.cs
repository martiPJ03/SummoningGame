using UnityEngine;

/// <summary>
/// Component per a cada unitat invocable (summon).
/// Responsabilitats:
///   · Pagar el cost inicial d'invocació (summonCost) en ser instanciat
///   · Registrar el drenatge de manteniment al ManaSystem mentre viu
///   · Desregistrar-se automàticament en morir o ser destruïda
///
/// SETUP:
///   Afegir aquest component a qualsevol Prefab de summon (AlliedUnit).
///   Configurar summonCost i maintenanceCostPerSecond a l'Inspector.
/// </summary>
[RequireComponent(typeof(AlliedUnit))]
public class UnitManaCost : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    //  CONFIGURACIÓ (Inspector)
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Cost d'invocació")]
    [Tooltip("Maná gastat una sola vegada en invocar la unitat")]
    public float summonCost = 20f;

    [Header("Cost de manteniment")]
    [Tooltip("Maná gastat per segon mentre la unitat és viva")]
    public float maintenanceCostPerSecond = 2f;

    [Header("Comportament")]
    [Tooltip("Si no hi ha prou maná per invocar, destrueix la unitat automàticament")]
    public bool destroyIfCannotAfford = true;

    // ─────────────────────────────────────────────────────────────────────────
    //  PROPIETATS (per al ManaSystem)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Cost de manteniment per segon (llegit per ManaSystem cada frame).</summary>
    public float MaintenanceCostPerSecond => maintenanceCostPerSecond;

    // ─────────────────────────────────────────────────────────────────────────
    //  ESTAT INTERN
    // ─────────────────────────────────────────────────────────────────────────

    private AlliedUnit _unit;
    private bool _registered = false;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _unit = GetComponent<AlliedUnit>();
        _unit.onDeath.AddListener(OnUnitDied);
    }

    void Start()
    {
        // Start() garanteix que ManaSystem.Instance ja existeix si és al mateix frame
        PaySummonCost();
    }

    void OnDestroy()
    {
        // Garantia de neteja en qualsevol cas (destrucció forçada, canvi d'escena, etc.)
        Unregister();

        if (_unit != null)
            _unit.onDeath.RemoveListener(OnUnitDied);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  LÒGICA DE PAGAMENT
    // ─────────────────────────────────────────────────────────────────────────

    void PaySummonCost()
    {
        if (ManaManager.Instance == null)
        {
            Debug.LogWarning("[UnitManaCost] ManaSystem no trobat a l'escena. " +
                             "Invocant sense cost.");
            Register();
            return;
        }

        // Intentar pagar el cost inicial
        if (summonCost > 0f)
        {
            bool success = ManaManager.Instance.TrySpendMana(summonCost);

            if (!success)
            {
                Debug.LogWarning($"[UnitManaCost] Maná insuficient per invocar '{gameObject.name}'. " +
                                 $"Necessari: {summonCost}, Disponible: " +
                                 $"{ManaManager.Instance.CurrentMana:F1}");

                if (destroyIfCannotAfford)
                {
                    Destroy(gameObject);
                    return; // Aturar execució; OnDestroy() netejarà
                }
            }
        }

        // Registrar drenatge de manteniment
        Register();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  REGISTRE / DESREGISTRE
    // ─────────────────────────────────────────────────────────────────────────

    void Register()
    {
        if (_registered) return;

        ManaManager.Instance?.RegisterConsumer(this);
        _registered = true;
    }

    void Unregister()
    {
        if (!_registered) return;

        ManaManager.Instance?.UnregisterConsumer(this);
        _registered = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CALLBACKS D'UNITAT
    // ─────────────────────────────────────────────────────────────────────────

    void OnUnitDied(Unit unit)
    {
        // La unitat ha mort: deixa de consumir maná immediatament
        Unregister();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA (útil per a reinvocació / evolució)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Força el desregistre del drenatge sense destruir la unitat.
    /// Útil quan una unitat es "desmaterialitza" temporalment.
    /// </summary>
    public void Suspend()
    {
        Unregister();
    }

    /// <summary>
    /// Torna a registrar el drenatge sense pagar cost d'invocació.
    /// Útil per tornar a activar una unitat suspesa.
    /// </summary>
    public void Resume()
    {
        Register();
    }
}
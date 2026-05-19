using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Barra inferior HUD que muestra retratos de todas las unidades aliadas.
/// Sincroniza el estado de selección con SelectionManager.
///
/// SETUP:
///   1. Crear un Canvas (Screen Space - Overlay) con un panel anclado abajo.
///   2. Añadir este componente a un GameObject "UnitHUDBar" en la escena.
///   3. Asignar:
///       · portraitContainer  → HorizontalLayoutGroup padre de las tarjetas
///       · cardPrefab         → prefab con UnitPortraitCard (ver UnitPortraitCard.cs)
///   4. Las unidades se registran automáticamente si llaman a RegisterUnit()
///      desde su Awake, o las buscas en Start() con auto-discover.
///
/// FLUJO:
///   · AlliedUnit aparece → RegisterUnit(unit)
///   · SelectionManager cambia → SyncSelectionVisuals()
///   · UnitPortraitCard.OnPointerClick → SelectionManager → OnSelectionChanged
/// </summary>
public class UnitHUDBar : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static UnitHUDBar Instance { get; private set; }

    // ── Referencias ──────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Transform con HorizontalLayoutGroup donde se instancian las tarjetas")]
    public Transform portraitContainer;

    [Tooltip("Prefab UnitPortraitCard")]
    public GameObject cardPrefab;

    [Header("Settings")]
    [Tooltip("Si true, busca automáticamente unidades aliadas en Start()")]
    public bool autoDiscover = true;

    // ── Estado interno ────────────────────────────────────────────────────────

    private readonly List<UnitPortraitCard> _cards = new List<UnitPortraitCard>();
    private readonly Dictionary<AlliedUnit, UnitPortraitCard> _unitToCard = new Dictionary<AlliedUnit, UnitPortraitCard>();

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (autoDiscover)
            DiscoverUnits();

        // Escuchar cambios de selección para refrescar los marcos de las tarjetas
        // Hacemos polling en LateUpdate en lugar de evento para evitar dependencia circular
    }

    void LateUpdate()
    {
        SyncSelectionVisuals();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  REGISTRO DE UNIDADES
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registra una unidad aliada y crea su tarjeta en el HUD.
    /// Llamar desde AlliedUnit.Awake() o desde CombatManager al invocar.
    /// </summary>
    public void RegisterUnit(AlliedUnit unit)
    {
        if (unit == null || _unitToCard.ContainsKey(unit)) return;
        if (cardPrefab == null || portraitContainer == null)
        {
            Debug.LogWarning("[UnitHUDBar] cardPrefab o portraitContainer no asignado.");
            return;
        }

        var go = Instantiate(cardPrefab, portraitContainer);
        var card = go.GetComponent<UnitPortraitCard>();
        if (card == null)
        {
            Debug.LogError("[UnitHUDBar] cardPrefab no tiene UnitPortraitCard.");
            Destroy(go);
            return;
        }

        card.Initialize(unit);
        _cards.Add(card);
        _unitToCard[unit] = card;

        // Escuchar muerte para reordenar (muertos al final)
        unit.onDeath.AddListener(_ => OnUnitDied(unit));

        Debug.Log($"[UnitHUDBar] Unidad registrada: {unit.unitName}");
    }

    /// <summary>Elimina la tarjeta de una unidad del HUD (si la unidad desaparece de la escena).</summary>
    public void UnregisterUnit(AlliedUnit unit)
    {
        if (unit == null || !_unitToCard.TryGetValue(unit, out var card)) return;

        _cards.Remove(card);
        _unitToCard.Remove(unit);
        if (card != null) Destroy(card.gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SINCRONIZACIÓN CON SELECTIONMANAGER
    // ─────────────────────────────────────────────────────────────────────────

    private void SyncSelectionVisuals()
    {
        if (SelectionManager.Instance == null) return;

        var selected = SelectionManager.Instance.Selected;
        foreach (var card in _cards)
        {
            if (card == null) continue;
            bool isSel = card.LinkedUnit != null && selected.Contains(card.LinkedUnit);
            card.SetSelected(isSel);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MUERTE
    // ─────────────────────────────────────────────────────────────────────────

    private void OnUnitDied(AlliedUnit unit)
    {
        // Mover la tarjeta de la unidad muerta al final del contenedor
        if (!_unitToCard.TryGetValue(unit, out var card)) return;
        card.transform.SetAsLastSibling();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  AUTO-DISCOVER
    // ─────────────────────────────────────────────────────────────────────────

    private void DiscoverUnits()
    {
        AlliedUnit[] units = FindObjectsByType<AlliedUnit>(FindObjectsSortMode.None);
        PlayerUnit[] playerUnit = FindObjectsByType<PlayerUnit>(FindObjectsSortMode.None);
        foreach (AlliedUnit unit in units)
        {
            if (unit.side == UnitSide.Player && !unit.IsDead)
                RegisterUnit(unit);
        }
        foreach (PlayerUnit unit in playerUnit)
        {
            if (!unit.IsDead)
                RegisterUnit(unit);
        }
        Debug.Log($"[UnitHUDBar] Auto-discover: {units.Length} unidades encontradas.");
    }
}
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

    [Header("Card Sizing")]
    [Tooltip("Mida original de la carta (ha de coincidir amb el prefab)")]
    public float cardBaseWidth = 120f;
    [Tooltip("Mida mínima a la qual es pot comprimir una carta")]
    public float cardMinWidth = 48f;
    [Tooltip("Separació base entre cartes")]
    public float spacingBase = 8f;
    [Tooltip("Separació mínima entre cartes")]
    public float spacingMin = -16f;
    [Tooltip("Layout Group del contenedor de cartes")]
    public HorizontalLayoutGroup _layoutGroup;
    [Tooltip("Rect Transform del propio HUD")]
    public RectTransform portraitContainerRect;

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
        RefreshCardSizes();
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

    // ─────────────────────────────────────────────────────────────────────────
    //  REFRESCO DE TAMAÑOS (POR EJEMPLO, PARA DESTACAR SELECCIÓN)
    // ─────────────────────────────────────────────────────────────────────────
    void RefreshCardSizes()
    {
        int count = _cards.Count(c => c != null);
        if (count == 0) return;

        // Amplada disponible del HUD
        float availableWidth = portraitContainerRect.rect.width;

        // Amplada total que necessitaríem sense comprimir
        float totalNatural = cardBaseWidth * count + spacingBase * (count - 1);

        float targetCardWidth;
        float targetSpacing;

        if (totalNatural <= availableWidth)
        {
            // Hi ha espai suficient: mida natural
            targetCardWidth = cardBaseWidth;
            targetSpacing = spacingBase;
        }
        else
        {
            // Cal comprimir: primer reduïm spacing, després la mida de la carta
            // Intentem primer amb spacing mínim
            float totalMinSpacing = cardBaseWidth * count + spacingMin * (count - 1);

            if (totalMinSpacing <= availableWidth)
            {
                // Només cal ajustar el spacing
                targetCardWidth = cardBaseWidth;
                targetSpacing = (availableWidth - cardBaseWidth * count) / Mathf.Max(1, count - 1);
                targetSpacing = Mathf.Max(targetSpacing, spacingMin);
            }
            else
            {
                // Cal comprimir les cartes
                targetSpacing = spacingMin;
                float spaceForCards = availableWidth - spacingMin * (count - 1);
                targetCardWidth = Mathf.Max(cardMinWidth, spaceForCards / count);
            }
        }

        // Aplicar spacing al LayoutGroup
        if (_layoutGroup != null)
            _layoutGroup.spacing = targetSpacing;

        // Aplicar amplada a cada carta
        foreach (var card in _cards)
        {
            if (card == null) continue;
            var rt = card.GetComponent<RectTransform>();
            if (rt == null) continue;

            // Només actualitzem si ha canviat per evitar recalculs de layout constants
            if (!Mathf.Approximately(rt.sizeDelta.x, targetCardWidth))
                rt.sizeDelta = new Vector2(targetCardWidth, rt.sizeDelta.y);
        }
    }
}
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Tarjeta de retrato de unidad en la barra inferior HUD.
/// Muestra icono, nombre, barra de vida y estado muerto/vivo.
/// Es clicable: click selecciona/deselecciona, Ctrl+click añade a selección.
///
/// SETUP:
///   Prefab con estructura:
///     UnitPortraitCard (Image + CanvasGroup + este script)
///       ├── Portrait (Image)          ← imagen/icono de la unidad
///       ├── SelectionFrame (Image)    ← borde verde cuando está seleccionado
///       ├── DeadOverlay (Image)       ← overlay oscuro cuando está muerto
///       ├── NameText (TMP_Text)       ← nombre de la unidad
///       └── HealthBarFill (Image)     ← barra de vida (Image.fillAmount)
/// </summary>
public class UnitPortraitCard : MonoBehaviour, IPointerClickHandler
{
    // ── Referencias UI ────────────────────────────────────────────────────────

    [Header("UI References")]
    public Image portraitImage;
    public Image selectionFrame;
    public Image deadOverlay;
    public TMPro.TMP_Text nameText;
    public Image healthBarFill;

    [Header("Colors")]
    public Color alivePortraitTint = Color.white;
    public Color deadPortraitTint = new Color(0.25f, 0.25f, 0.25f, 1f);
    public Color healthColor = new Color(0.29f, 0.87f, 0.41f, 1f);
    public Color deadHealthColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    // ── Estado interno ────────────────────────────────────────────────────────

    private AlliedUnit _unit;
    private bool _isSelected;

    // ─────────────────────────────────────────────────────────────────────────
    //  INICIALIZACIÓN
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Asignar la unidad que representa esta tarjeta.</summary>
    public void Initialize(AlliedUnit unit)
    {
        _unit = unit;

        // Nombre
        if (nameText != null)
            nameText.text = unit.unitName;

        // Escuchar cambios de vida
        unit.onHealthChanged.AddListener(OnHealthChanged);

        // Escuchar muerte
        unit.onDeath.AddListener(OnUnitDied);

        // Estado inicial
        RefreshHealthBar(unit.stats.currentHealth, unit.stats.maxHealth);
        SetDead(false);
        SetSelected(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CLICK
    // ─────────────────────────────────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_unit == null || _unit.IsDead) return;

        bool ctrl = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;

        if (ctrl)
        {
            // Ctrl + click: toggle dentro de la selección actual
            if (_isSelected)
                SelectionManager.Instance?.RemoveFromSelection(_unit);
            else
                SelectionManager.Instance?.AddToSelection(_unit);
        }
        else
        {
            // Click simple: toggle si ya está solo, o selección exclusiva
            if (_isSelected && SelectionManager.Instance?.Selected.Count == 1)
                SelectionManager.Instance?.DeselectAll();
            else
                SelectionManager.Instance?.SelectOnly(_unit);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA (llamada por UnitHUDBar)
    // ─────────────────────────────────────────────────────────────────────────

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        if (selectionFrame != null)
            selectionFrame.enabled = selected;
    }

    public AlliedUnit LinkedUnit => _unit;

    // ─────────────────────────────────────────────────────────────────────────
    //  EVENTOS DE UNIDAD
    // ─────────────────────────────────────────────────────────────────────────

    private void OnHealthChanged(float current, float max)
    {
        RefreshHealthBar(current, max);
    }

    private void OnUnitDied(Unit unit)
    {
        SetDead(true);
        SetSelected(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  VISUAL
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshHealthBar(float current, float max)
    {
        if (healthBarFill == null || max <= 0f) return;

        float pct = Mathf.Clamp01(current / max);
        healthBarFill.fillAmount = pct;

        healthBarFill.color = healthColor;

    }

    private void SetDead(bool dead)
    {
        // Overlay oscuro sobre el retrato
        if (deadOverlay != null)
            deadOverlay.enabled = dead;

        // Tint del retrato en gris
        if (portraitImage != null)
            portraitImage.color = dead ? deadPortraitTint : alivePortraitTint;

        // Barra de vida gris
        if (healthBarFill != null && dead)
        {
            healthBarFill.fillAmount = 1f;
            healthBarFill.color = deadHealthColor;
        }

        // Desactivar interacción con la tarjeta
        var canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null)
            canvasGroup.interactable = !dead;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CLEANUP
    // ─────────────────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        if (_unit == null) return;
        _unit.onHealthChanged.RemoveListener(OnHealthChanged);
        _unit.onDeath.RemoveListener(OnUnitDied);
    }
}
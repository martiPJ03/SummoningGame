using NUnit.Framework.Internal;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.UIElements.Experimental;

/// <summary>
/// Gestiona toda la selección de unidades del jugador al estilo RTS:
///   · Click simple         → seleccionar una unidad
///   · Shift + Click        → añadir / quitar de la selección
///   · Drag                 → seleccionar todas las unidades dentro del rectángulo
///   · Shift + Drag         → añadir las del rectángulo a la selección actual
///   · Click derecho        → orden de movimiento (terreno) o ataque (enemigo)
///   · Ctrl + A             → seleccionar todas las unidades del jugador
///   · Escape / Click vacío → deseleccionar todo
///
/// Requiere:
///   · Un Canvas en la escena con un child Image asignado a <selectionBoxImage>
///     (Color semi-transparente, sin RaycastTarget)
///   · Las unidades en una Layer "Unit" para el OverlapBox
/// </summary>
public class SelectionManager : MonoBehaviour
{
    public static SelectionManager Instance { get; private set; }

    // ── Referencias ───────────────────────────────────────────────────────────

    [Header("UI del rectángulo de selección")]
    [Tooltip("Image hijo del Canvas que se estira para mostrar el drag rect")]
    public RectTransform selectionBoxRect;

    [Tooltip("Layer mask with 'PlayerUnit' and 'EnemyUnit' layers for overlap queries")]
    public LayerMask unitLayerMask;

    // ── Estado interno ────────────────────────────────────────────────────────

    private readonly List<Unit> _selected = new List<Unit>();

    private Camera _cam;
    private Vector2 _dragOriginScreen;   // píxeles pantalla donde empezó el drag
    private bool _isDragging;

    // Un drag solo se activa si el ratón se mueve más de este umbral (evita
    // que un click normal active el rectángulo si hay un leve temblor)
    private const float DragThreshold = 6f;

    // ── Nuevas variables de estado para formation drag (botón derecho) ────────────
    private bool _isRightDragging = false;
    private bool _rightDragThresholdMet = false;
    private Vector2 _rightDragOriginScreen;
    private Vector2 _rightDragOriginWorld;
    private bool _rightClickWasOnEnemy = false;
    private Unit _rightClickEnemy = null;

    private const float RightDragThresholdPx = 8f;   // píxeles mínimos para activar formation drag
    private const float MinUnitSpacing = 0.9f;  // separación mínima entre unidades


    // ── Acceso de solo lectura a la selección actual ──────────────────────────

    public IReadOnlyList<Unit> Selected => _selected;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _cam = Camera.main;

        // Assign both PlayerUnit and EnemyUnit layers to unitLayerMask
        int playerUnitLayer = LayerMask.NameToLayer("PlayerUnit");
        int enemyUnitLayer = LayerMask.NameToLayer("EnemyUnit");
        unitLayerMask = (1 << playerUnitLayer) | (1 << enemyUnitLayer);

        if (selectionBoxRect != null)
            selectionBoxRect.gameObject.SetActive(false);
    }

    void Update()
    {
        HandleSelectionInput();
        HandleOrderInput();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  INPUT DE SELECCIÓN  (botón izquierdo)
    // ─────────────────────────────────────────────────────────────────────────

    void HandleSelectionInput()
    {
        // ── Inicio del drag ───────────────────────────────────────────────────
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            _dragOriginScreen = Mouse.current.position.ReadValue();
            _isDragging = false;
        }

        // ── Mantener botón: comprobar si pasamos el umbral de drag ────────────
        if (Mouse.current.leftButton.isPressed)
        {
            bool thresholdReached = Vector2.Distance(Mouse.current.position.ReadValue(), _dragOriginScreen) > DragThreshold;

            if (!_isDragging && thresholdReached)
            {
                _isDragging = true;
                if (selectionBoxRect != null)
                    selectionBoxRect.gameObject.SetActive(true);
            }

            if (_isDragging)
                UpdateSelectionBoxUI();
        }

        // ── Soltar botón ──────────────────────────────────────────────────────
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            if (_isDragging)
                FinalizeDragSelection();
            else
                HandleSingleClick();

            _isDragging = false;
            if (selectionBoxRect != null)
                selectionBoxRect.gameObject.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CLICK SIMPLE
    // ─────────────────────────────────────────────────────────────────────────

    void HandleSingleClick()
    {
        Vector2 worldPos = ScreenToWorld(_dragOriginScreen);
        Unit clickedUnit = GetUnitAtWorldPos(worldPos);

        bool shift = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;

        if (clickedUnit != null && clickedUnit.side == UnitSide.Player && !clickedUnit.IsDead)
        {
            if (shift)
                ToggleUnit(clickedUnit);     // Shift+click → toggle
            else
                SelectOnly(clickedUnit);     // Click normal → selección exclusiva
        }
        else
        {
            // Click en vacío (o en enemigo sin orden de ataque aquí)
            if (!shift)
                DeselectAll();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DRAG SELECTION
    // ─────────────────────────────────────────────────────────────────────────

    void FinalizeDragSelection()
    {
        bool shift = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;

        if (!shift) DeselectAll();

        // Calcular el rectángulo en espacio mundo
        Vector2 originWorld = ScreenToWorld(_dragOriginScreen);
        Vector2 currentWorld = ScreenToWorld(Mouse.current.position.ReadValue());

        Vector2 center = (originWorld + currentWorld) * 0.5f;
        Vector2 size = new Vector2(
            Mathf.Abs(currentWorld.x - originWorld.x),
            Mathf.Abs(currentWorld.y - originWorld.y)
        );

        // OverlapBox para encontrar todos los colliders dentro del rectángulo
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, size, 0f, unitLayerMask);

        foreach (var collider in hits)
        {
            Unit unit = collider.GetComponent<Unit>();
            if (unit != null && unit.side == UnitSide.Player && !unit.IsDead)
                AddToSelection(unit);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ÓRDENES  (botón derecho)
    // ─────────────────────────────────────────────────────────────────────────

    void HandleOrderInput()
    {
        // Ctrl + A → seleccionar todas
        if (Keyboard.current.leftCtrlKey.isPressed && Keyboard.current.aKey.wasPressedThisFrame)
        {
            SelectAll();
            return;
        }

        // Escape → deseleccionar
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            DeselectAll();
            return;
        }

        if (_selected.Count == 0) return;

        // ── MouseRightButton registrar origen, detectar si es enemigo ──────────
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            _rightDragOriginScreen = Mouse.current.position.ReadValue();
            _rightDragOriginWorld = ScreenToWorld(_rightDragOriginScreen);
            _isRightDragging = true;
            _rightDragThresholdMet = false;

            Unit clicked = GetUnitAtWorldPos(_rightDragOriginWorld);
            _rightClickWasOnEnemy = clicked != null
                                 && clicked.side == UnitSide.Enemy
                                 && !clicked.IsDead;
            _rightClickEnemy = _rightClickWasOnEnemy ? clicked : null;
        }

        // ── MouseRightButton mantenido: comprobar si supera el umbral de drag ───────
        if (_isRightDragging && Mouse.current.rightButton.isPressed)
        {
            float distanceX = Vector2.Distance(Mouse.current.position.ReadValue(), _rightDragOriginScreen);
            if (!_rightDragThresholdMet && distanceX > RightDragThresholdPx)
            {
                if (!_rightClickWasOnEnemy)
                    _rightDragThresholdMet = true;
            }

            // Preview en tiempo real mientras se arrastra
            if (_rightDragThresholdMet)
            {
                Vector2 dragEnd = ScreenToWorld(Mouse.current.position.ReadValue());
                Vector2 dragVec = dragEnd - _rightDragOriginWorld;
                IssueMoveOrderFormation(_rightDragOriginWorld, dragVec, preview: true);
            }
        }

        // ── MouseRightButton: resolver la orden ──────────────────────────────────
        if (Mouse.current.rightButton.wasReleasedThisFrame)
        {
            if (_rightClickWasOnEnemy)
            {
                IssueAttackOrder(_rightClickEnemy);
            }
            else if (_rightDragThresholdMet)
            {
                Vector2 dragEnd = ScreenToWorld(Mouse.current.position.ReadValue());
                Vector2 dragVec = dragEnd - _rightDragOriginWorld;
                IssueMoveOrderFormation(_rightDragOriginWorld, dragVec, preview: false);
            }
            else
            {
                IssueMoveOrderFormation(_rightDragOriginWorld, preview: false);
            }

            _isRightDragging = false;
            _rightDragThresholdMet = false;
            _rightClickWasOnEnemy = false;
            _rightClickEnemy = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ÓRDENES: MOVIMIENTO EN FORMACIÓN
    // ─────────────────────────────────────────────────────────────────────────

    void IssueMoveOrderFormation(Vector2 anchor, Vector2 dragVector = default, bool preview = false)
    {
        var units = GetAliveSelected();
        if (units.Count == 0) return;

        bool hasDrag = HasValidDrag(dragVector);

        Vector2 lineDir = GetLineDirection(dragVector, hasDrag);
        Vector2 facingDir = GetFacingDirection(dragVector, hasDrag);
        float spacing = CalculateSpacing(dragVector, units.Count, hasDrag);

        for (int i = 0; i < units.Count; i++)
        {
            Vector2 destination = CalculateDestination(anchor, i, units.Count, hasDrag, lineDir, spacing);
            HandleUnitOrder(units[i], destination, facingDir, hasDrag, preview);
        }
    }
    bool HasValidDrag(Vector2 dragVector)
    {
        return dragVector != default && dragVector.sqrMagnitude > 0.001f;
    }

    Vector2 GetLineDirection(Vector2 dragVector, bool hasDrag)
    {
        return hasDrag ? dragVector.normalized : Vector2.right;
    }

    Vector2 GetFacingDirection(Vector2 dragVector, bool hasDrag)
    {
        return hasDrag ? new Vector2(-dragVector.y, dragVector.x).normalized : Vector2.zero;
    }

    float CalculateSpacing(Vector2 dragVector, int unitCount, bool hasDrag)
    {
        if (unitCount <= 1) return 0f;

        if (hasDrag) return Mathf.Max(MinUnitSpacing, dragVector.magnitude / (unitCount - 1));

        return MinUnitSpacing;
    }

    Vector2 CalculateDestination(Vector2 anchor, int index, int total, bool hasDrag, Vector2 lineDir, float spacing)
    {
        if (hasDrag) return anchor + lineDir * (spacing * index);

        return anchor + FormationOffset(index, total);
    }

    Vector2 FormationOffset(int index, int total)
    {
        if (total == 1) return Vector2.zero;

        int cols = Mathf.CeilToInt(Mathf.Sqrt(total));
        int rows = Mathf.CeilToInt((float)total / cols);

        int row = index / cols;
        int col = index % cols;

        float spacing = MinUnitSpacing;

        Vector2 originOffset = new Vector2(
            -(cols - 1) * spacing * 0.5f,
            -(rows - 1) * spacing * 0.5f
        );

        return originOffset + new Vector2(col * spacing, row * spacing);
    }

    void HandleUnitOrder(Unit unit, Vector2 destination, Vector2 facingDir, bool hasDrag, bool preview)
    {
        var indicator = unit.GetComponent<OrderIndicator>();

        if (preview)
        {
            indicator?.ShowMove(destination, facingDir, preview);
            return;
        }

        Vector2 finalFacing = hasDrag ? facingDir : Vector2.zero;

        TacticalPauseManager.Instance.IssueMoveOrder(unit, destination, finalFacing);
        
        indicator?.ShowMove(destination, finalFacing, preview);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ÓRDENES: ATAQUE
    // ─────────────────────────────────────────────────────────────────────────

    void IssueAttackOrder(Unit target)
    {
        foreach (var unit in GetAliveSelected())
        {
            TacticalPauseManager.Instance.IssueAttackOrder(unit, target);
            // Indicador: línea roja + espada
            var indicator = unit.GetComponent<OrderIndicator>();
            if (indicator != null)
            {
                Vector2 facing = (target.transform.position - unit.transform.position).normalized;
                indicator.ShowAttack(target);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GESTIÓN DE LA SELECCIÓN
    // ─────────────────────────────────────────────────────────────────────────

    void SelectOnly(Unit unit)
    {
        DeselectAll();
        AddToSelection(unit);
    }

    void ToggleUnit(Unit unit)
    {
        if (_selected.Contains(unit))
            RemoveFromSelection(unit);
        else
            AddToSelection(unit);
    }

    void AddToSelection(Unit unit)
    {
        if (_selected.Contains(unit)) return;
        _selected.Add(unit);
        unit.SetSelected(true);
    }

    /// <summary>Llamado externamente cuando una unidad muere para limpiar la lista.</summary>
    public void RemoveFromSelection(Unit unit)
    {
        if (!_selected.Contains(unit)) return;
        _selected.Remove(unit);
        unit.SetSelected(false);
    }

    public void DeselectAll()
    {
        // Iterar sobre copia para evitar modificar la lista mientras iteramos
        foreach (var selectedUnit in _selected.ToArray())
            if (selectedUnit != null) selectedUnit.SetSelected(false);

        _selected.Clear();
    }

    void SelectAll()
    {
        // Buscamos todas las unidades jugador vivas en la escena
        foreach (var foundUnit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
            if (foundUnit.side == UnitSide.Player && !foundUnit.IsDead)
                AddToSelection(foundUnit);
    }

    List<Unit> GetAliveSelected()
    {
        _selected.RemoveAll(unit => unit == null || unit.IsDead);
        return _selected;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI: RECTÁNGULO DE DRAG
    // ─────────────────────────────────────────────────────────────────────────

    void UpdateSelectionBoxUI()
    {
        if (selectionBoxRect == null) return;

        Vector2 current = Mouse.current.position.ReadValue();

        // Tamaño y posición en píxeles de pantalla
        float width = Mathf.Abs(current.x - _dragOriginScreen.x);
        float height = Mathf.Abs(current.y - _dragOriginScreen.y);

        // El pivot de selectionBoxRect debe estar en (0, 0) — esquina inferior izquierda
        // Usamos anchoredPosition relativa a la esquina inferior izquierda del Canvas
        float anchoredPosX = Mathf.Min(current.x, _dragOriginScreen.x);
        float anchoredPosY = Mathf.Min(current.y, _dragOriginScreen.y);

        selectionBoxRect.sizeDelta = new Vector2(width, height);
        selectionBoxRect.anchoredPosition = new Vector2(anchoredPosX, anchoredPosY);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UTILIDADES
    // ─────────────────────────────────────────────────────────────────────────

    Vector2 ScreenToWorld(Vector2 screenPos)
    {
        return _cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, _cam.nearClipPlane));
    }

    Unit GetUnitAtWorldPos(Vector2 worldPos)
    {
        // Primero intentamos usar la LayerMask configurada (más eficiente).
        Collider2D hit = Physics2D.OverlapCircle(worldPos, 0.35f, unitLayerMask);
        if (hit != null)
            return hit.GetComponent<Unit>();

        // Si no encontramos nada con la mask (p.ej. en caso de que las unidades
        // enemigas estén en otra layer), hacemos un fallback comprobando todos
        // los colliders cercanos y buscando un componente Unit.
        Collider2D[] hits = Physics2D.OverlapCircleAll(worldPos, 0.35f);
        foreach (var hitCollider in hits)
        {
            var foundUnit = hitCollider.GetComponent<Unit>();
            if (foundUnit != null) return foundUnit;
        }

        return null;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!_isDragging || _cam == null) return;

        Vector2 originWorld = ScreenToWorld(_dragOriginScreen);
        Vector2 currentWorld = ScreenToWorld(Mouse.current.position.ReadValue());
        Vector2 center = (originWorld + currentWorld) * 0.5f;
        Vector2 size = new Vector2(
            Mathf.Abs(currentWorld.x - originWorld.x),
            Mathf.Abs(currentWorld.y - originWorld.y)
        );

        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.2f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.8f);
        Gizmos.DrawWireCube(center, size);
    }
#endif
}
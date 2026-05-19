using NUnit.Framework.Internal;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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

    private readonly List<AlliedUnit> _selected = new List<AlliedUnit>();

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

    // ── Path drawing (Shift + Right Click) ────────────────────────────────────
    private bool _isPathDrawing = false;
    private List<Vector2> _pathPoints = new List<Vector2>();
    const float PathPointSpacing = 0.6f;  // distancia mínima entre puntos del path


    // ── Acceso de solo lectura a la selección actual ──────────────────────────

    public IReadOnlyList<AlliedUnit> Selected => _selected;

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
            else if (!EventSystem.current.IsPointerOverGameObject())
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
        AlliedUnit clickedUnit = GetAlliedUnitAtWorldPos(worldPos);

        bool shift = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;

        if (clickedUnit != null && !clickedUnit.IsDead)
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
            AlliedUnit unit = collider.GetComponent<AlliedUnit>();
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

        bool shift = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;

        // ── Shift + Right Click para dibujar path ─────────────────────────────────
        if (shift && Mouse.current.rightButton.wasPressedThisFrame)
        {
            _isPathDrawing = true;
            _pathPoints.Clear();
            Vector2 worldPos = ScreenToWorld(Mouse.current.position.ReadValue());
            _pathPoints.Add(worldPos);
        }

        if (_isPathDrawing && shift && Mouse.current.rightButton.isPressed)
        {
            Vector2 currentPos = ScreenToWorld(Mouse.current.position.ReadValue());

            // Agregar punto si está lo suficientemente lejos del último
            if (_pathPoints.Count == 0 || Vector2.Distance(currentPos, _pathPoints[_pathPoints.Count - 1]) > PathPointSpacing)
            {
                _pathPoints.Add(currentPos);
            }
        }

        if (_isPathDrawing && shift && Mouse.current.rightButton.wasReleasedThisFrame)
        {
            // Terminar el path drawing y enviar la orden
            if (_pathPoints.Count > 0)
            {
                IssuePathFollowOrder(_pathPoints);
            }

            _isPathDrawing = false;
            _pathPoints.Clear();
        }

        // ── Right Click normal (sin Shift) para movimiento en formación ──────────────
        if (!shift && Mouse.current.rightButton.wasPressedThisFrame)
        {
            _rightDragOriginScreen = Mouse.current.position.ReadValue();
            _rightDragOriginWorld = ScreenToWorld(_rightDragOriginScreen);
            _isRightDragging = true;
            _rightDragThresholdMet = false;

            Unit clicked = GetEnemyUnitAtWorldPos(_rightDragOriginWorld);
            _rightClickWasOnEnemy = clicked != null && !clicked.IsDead;
            _rightClickEnemy = _rightClickWasOnEnemy ? clicked : null;
        }

        // ── MouseRightButton mantenido: comprobar si supera el umbral de drag ───────
        if (_isRightDragging && !shift && Mouse.current.rightButton.isPressed)
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
        if (!shift && Mouse.current.rightButton.wasReleasedThisFrame)
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

        unit.OrderMoveTo(destination, finalFacing);
        indicator?.ShowMove(destination, finalFacing, preview);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ÓRDENES: SEGUIMIENTO DE PATH
    // ─────────────────────────────────────────────────────────────────────────

    void IssuePathFollowOrder(List<Vector2> pathPoints)
    {
        if (pathPoints == null || pathPoints.Count < 2) return;

        foreach (var unit in GetAliveSelected())
        {
            OrderUnitFollowPath(unit, pathPoints);
        }
    }

    void OrderUnitFollowPath(Unit unit, List<Vector2> pathPoints)
    {
        if (unit == null || unit.IsDead) return;

        // Enviar la orden de seguir el path
        unit.OrderFollowPath(pathPoints);

        // Mostrar indicador de orden (path line completo)
        var indicator = unit.GetComponent<OrderIndicator>();
        if (indicator != null)
        {
            // Mostrar el destino final del path con toda la ruta multi-waypoint
            Vector3 finalDestination = new Vector3(pathPoints[pathPoints.Count - 1].x, pathPoints[pathPoints.Count - 1].y, 0);
            indicator.ShowMultiWaypointMove(finalDestination, pathPoints);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ÓRDENES: ATAQUE
    // ─────────────────────────────────────────────────────────────────────────

    void IssueAttackOrder(Unit target)
    {
        foreach (var unit in GetAliveSelected())
        {
            unit.OrderAttack(target);
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

    public void SelectOnly(AlliedUnit unit)
    {
        DeselectAll();
        AddToSelection(unit);
    }

    void ToggleUnit(AlliedUnit unit)
    {
        if (_selected.Contains(unit))
            RemoveFromSelection(unit);
        else
            AddToSelection(unit);
    }

    public void AddToSelection(AlliedUnit unit)
    {
        if (_selected.Contains(unit)) return;
        _selected.Add(unit);
        unit.SetSelected(true);
    }

    /// <summary>Llamado externamente cuando una unidad muere para limpiar la lista.</summary>
    public void RemoveFromSelection(AlliedUnit unit)
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
        foreach (var foundUnit in FindObjectsByType<AlliedUnit>(FindObjectsSortMode.None))
            if (foundUnit.side == UnitSide.Player && !foundUnit.IsDead)
                AddToSelection(foundUnit);
    }

    List<AlliedUnit> GetAliveSelected()
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

    AlliedUnit GetAlliedUnitAtWorldPos(Vector2 worldPos)
    {
        // Primero intentamos usar la LayerMask configurada (más eficiente).
        Collider2D hit = Physics2D.OverlapCircle(worldPos, 0.35f, unitLayerMask);
        if (hit != null)
        {
            AlliedUnit unit = hit.GetComponent<AlliedUnit>();
            if (unit != null) return unit;
        }

        // Si no encontramos nada con la mask, hacemos un fallback comprobando todos
        // los colliders cercanos buscando un componente AlliedUnit.
        Collider2D[] hits = Physics2D.OverlapCircleAll(worldPos, 0.35f);
        foreach (var hitCollider in hits)
        {
            var foundUnit = hitCollider.GetComponent<AlliedUnit>();
            if (foundUnit != null) return foundUnit;
        }

        return null;
    }

    Unit GetEnemyUnitAtWorldPos(Vector2 worldPos)
    {
        // Primero intentamos usar la LayerMask configurada (más eficiente).
        Collider2D hit = Physics2D.OverlapCircle(worldPos, 0.35f, unitLayerMask);
        if (hit != null)
        {
            Unit unit = hit.GetComponent<Unit>();
            if (unit != null && unit.side == UnitSide.Enemy) return unit;
        }

        // Si no encontramos nada con la mask, hacemos un fallback comprobando todos
        // los colliders cercanos buscando un componente Unit con side Enemy.
        Collider2D[] hits = Physics2D.OverlapCircleAll(worldPos, 0.35f);
        foreach (var hitCollider in hits)
        {
            var foundUnit = hitCollider.GetComponent<Unit>();
            if (foundUnit != null && foundUnit.side == UnitSide.Enemy) return foundUnit;
        }

        return null;
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
        if (!_isDragging && !_isPathDrawing) return;
        if (_cam == null) _cam = Camera.main;

        // Visualizar drag selection rect
        if (_isDragging)
        {
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

        // Visualizar path drawing
        if (_isPathDrawing && _pathPoints.Count > 0)
        {
            Gizmos.color = new Color(1f, 0.84f, 0f, 0.8f);  // Amarillo dorado

            // Dibujar puntos del path
            for (int i = 0; i < _pathPoints.Count; i++)
            {
                Vector3 pos = new Vector3(_pathPoints[i].x, _pathPoints[i].y, -0.1f);
                Gizmos.DrawSphere(pos, 0.15f);

                // Dibujar línea entre puntos consecutivos
                if (i < _pathPoints.Count - 1)
                {
                    Vector3 nextPos = new Vector3(_pathPoints[i + 1].x, _pathPoints[i + 1].y, -0.1f);
                    Gizmos.DrawLine(pos, nextPos);
                }
            }

            // Dibujar línea hacia el mouse actual mientras dibuja
            Vector2 currentMouse = ScreenToWorld(Mouse.current.position.ReadValue());
            Vector3 currentMousePos = new Vector3(currentMouse.x, currentMouse.y, -0.1f);
            Vector3 lastPathPoint = new Vector3(_pathPoints[_pathPoints.Count - 1].x, _pathPoints[_pathPoints.Count - 1].y, -0.1f);
            Gizmos.color = new Color(1f, 0.84f, 0f, 0.4f);  // Más transparente
            Gizmos.DrawLine(lastPathPoint, currentMousePos);
        }
    }
#endif
}
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Afegir aquest component als botons de Pausa i Resume.
/// S'integra amb TacticalPauseManager:
///   · Joc en marxa  → Pausa actiu  | Resume disabled + transparent
///   · Joc pausat    → Pausa disabled + transparent | Resume actiu
///
/// SETUP:
///   1. Afegir PauseButtonUI al botó de Pausa  → buttonType = Pause
///   2. Afegir PauseButtonUI al botó de Resume → buttonType = Resume
/// </summary>
public class PauseButtonUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public enum ButtonType { Pause, Resume }

    [Header("Configuració")]
    public ButtonType buttonType = ButtonType.Pause;

    [Header("Hover")]
    [Tooltip("Escala aplicada quan el ratolí passa per sobre")]
    public float hoverScale = 1.15f;

    [Tooltip("Velocitat del tween d'escala")]
    public float scaleSpeed = 10f;

    [Header("Estat disabled")]
    [Tooltip("Transparència quan el botó està desactivat (0 = invisible, 1 = opac)")]
    [Range(0f, 1f)]
    public float disabledAlpha = 0.35f;

    private RectTransform _rect;
    private Image _image;
    private Button _button;

    private Vector3 _baseScale;
    private Vector3 _targetScale;
    
    private bool _isSubscribed = false;

    void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _baseScale = _rect.localScale;
        _targetScale = _baseScale;

        _image = GetComponent<Image>();
        if (_image == null)
            _image = gameObject.AddComponent<Image>();

        _button = GetComponent<Button>();
    }

    void OnEnable()
    {
        TrySubscribeToPauseManager();
    }

    void OnDisable()
    {
        UnsubscribeToPauseManager();
    }

    void Update()
    {
        // unscaledDeltaTime perquè funcioni amb timeScale = 0 (durant la pausa)
        _rect.localScale = Vector3.Lerp(
            _rect.localScale,
            _targetScale,
            Time.unscaledDeltaTime * scaleSpeed
        );

        if (!_isSubscribed && TacticalPauseManager.Instance != null)
        {
            TrySubscribeToPauseManager();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;
        _targetScale = _baseScale * hoverScale;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _targetScale = _baseScale;
    }

    void OnGamePaused()
    {
        // Pausat: Resume actiu, Pausa desactivat
        SetButtonState(buttonType == ButtonType.Resume);
    }

    void OnGameResumed()
    {
        // En marxa: Pausa actiu, Resume desactivat
        SetButtonState(buttonType == ButtonType.Pause);
    }

    void SetButtonState(bool active)
    {
        if (_button != null)
            _button.interactable = active;

        if (_image != null)
            _image.color = new Color(_image.color.r, _image.color.g, _image.color.b, active ? 1f : disabledAlpha);

        if (!active)
            _targetScale = _baseScale;
    }

    private void TrySubscribeToPauseManager()
    {
        // CRÍTICO: Verifica el flag ANTES de suscribirse
        if (_isSubscribed)
            return;

        if (TacticalPauseManager.Instance != null)
        {
            TacticalPauseManager.Instance.OnPaused += OnGamePaused;
            TacticalPauseManager.Instance.OnResumed += OnGameResumed;
            _isSubscribed = true;

            // Sincronitzar amb l'estat actual
            if (TacticalPauseManager.Instance.IsPaused)
                OnGamePaused();
            else
                OnGameResumed();
        }
    }

    private void UnsubscribeToPauseManager()
    {
        if (!_isSubscribed)
            return;

        if (TacticalPauseManager.Instance != null)
        {
            TacticalPauseManager.Instance.OnPaused -= OnGamePaused;
            TacticalPauseManager.Instance.OnResumed -= OnGameResumed;
        }

        _isSubscribed = false;
    }
}
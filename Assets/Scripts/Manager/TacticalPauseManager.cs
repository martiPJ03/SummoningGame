using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Responsabilitat: gestionar el temps de joc (pausa tàctica).
///   · Congela l'escena (Time.timeScale = 0)
///   · Llança esdeveniments OnPaused / OnResumed perquè altres sistemes reaccionin
///   · Tecles: Space o P per toggle pausa/play
///
/// SETUP:
///   Afegir aquest component a un GameObject persistent (GameManager, etc.)
/// </summary>
public class TacticalPauseManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static TacticalPauseManager Instance { get; private set; }

    // ── Estat ─────────────────────────────────────────────────────────────────
    public bool IsPaused { get; private set; } = false;

    // ── Esdeveniments ─────────────────────────────────────────────────────────
    public event Action OnPaused;
    public event Action OnResumed;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        bool spacePressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        bool pPressed = Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame;

        if (spacePressed || pPressed)
            Toggle();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;
        Time.timeScale = 0f;
        OnPaused?.Invoke();
        Debug.Log("[TacticalPause] PAUSA activada");
    }

    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;
        Time.timeScale = 1f;
        OnResumed?.Invoke();
        Debug.Log("[TacticalPause] JOC reprès");
    }

    public void Toggle()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CLEANUP
    // ─────────────────────────────────────────────────────────────────────────

    void OnDestroy()
    {
        if (IsPaused)
            Time.timeScale = 1f;
    }
}
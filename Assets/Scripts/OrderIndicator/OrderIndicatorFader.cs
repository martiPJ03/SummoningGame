using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Responsabilitat ÚNICA: detectar l'arribada i aplicar el fade-out.
///   · Comprova cada frame si la unitat ha assolit el destí o l'objectiu
///   · Llança el fade usant temps no escalat (funciona durant la pausa)
///   · Notifica OrderIndicator quan el fade ha acabat via OnFadeComplete
///
/// SETUP: afegir al mateix GameObject que OrderIndicator.
/// </summary>
[RequireComponent(typeof(OrderIndicatorState))]
public class OrderIndicatorFader : MonoBehaviour
{
    [Header("Fade")]
    public float fadeDuration = 0.4f;
    public float fadeDelay = 0.15f;

    // Notifica al coordinador quan el fade ha acabat
    public System.Action OnFadeComplete;

    private Coroutine _fadeRoutine;
    private NavMeshAgent _agent;
    private Unit _unit;
    private OrderIndicatorState _state;

    // Subcomponents als quals apliquem l'alpha
    private OrderPathLine _pathLine;
    private OrderIcon _icon;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _state = GetComponent<OrderIndicatorState>();
        _agent = GetComponent<NavMeshAgent>();
        _unit = GetComponent<Unit>();
        _pathLine = GetComponent<OrderPathLine>();
        _icon = GetComponent<OrderIcon>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Comprova l'arribada i inicia el fade si escau. Cridar des d'OrderIndicator.LateUpdate.</summary>
    public void CheckArrival()
    {
        if (_fadeRoutine != null) return;
        if (!HasArrived()) return;

        _fadeRoutine = StartCoroutine(FadeOutRoutine());
    }

    /// <summary>Atura qualsevol fade en curs i reinicia l'alpha a 1.</summary>
    public void CancelFade()
    {
        if (_fadeRoutine == null) return;
        StopCoroutine(_fadeRoutine);
        _fadeRoutine = null;
        ApplyAlpha(1f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DETECCIÓ D'ARRIBADA
    // ─────────────────────────────────────────────────────────────────────────

    bool HasArrived()
    {
        bool agentDone = _agent != null
                              && !_agent.pathPending
                              && _agent.remainingDistance <= _agent.stoppingDistance;
        float dist = Vector2.Distance(transform.position, _state.Destination);
        switch (_state.CurrentOrder)
        {
            case OrderIndicatorState.OrderType.Move:
                return agentDone || dist < 0.25f;

            case OrderIndicatorState.OrderType.Attack:
                return _state.AttackTarget == null
                    || _state.AttackTarget.IsDead
                    || agentDone
                    || dist < 0.25f;

            default:
                return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  FADE ROUTINE
    // ─────────────────────────────────────────────────────────────────────────

    IEnumerator FadeOutRoutine()
    {
        yield return new WaitForSecondsRealtime(fadeDelay);

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            ApplyAlpha(Mathf.Lerp(1f, 0f, t / fadeDuration));
            yield return null;
        }

        _fadeRoutine = null;
        OnFadeComplete?.Invoke();
    }

    void ApplyAlpha(float alpha)
    {
        _pathLine?.ApplyAlpha(alpha);
        _icon?.ApplyAlpha(alpha);
    }
}
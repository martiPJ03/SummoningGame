using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Número flotante de daño que sube y hace fade-out.
/// 
/// SETUP:
///   1. Crear un prefab con un GameObject vacío.
///   2. Añadir un hijo con TextMeshPro - Text (UI) o TextMeshPro (world space).
///      Recomendado: TextMeshPro world space, sorting order 50.
///   3. Añadir este componente al GameObject raíz del prefab.
///   4. Asignar la referencia _tmp en el Inspector (o dejar que Awake lo busque).
///   5. Asignar el prefab a HitParticleEmitter.damageNumberPrefab.
/// </summary>
public class DamageNumberDisplay : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("TextMeshPro del hijo. Si es null lo busca automáticamente.")]
    public TextMeshPro label;

    [Header("Movimiento")]
    public float riseSpeed = 1.4f;   // unidades/s hacia arriba
    public float lifetime = 0.75f;  // segundos hasta desaparecer
    public float fadeStart = 0.45f;  // cuándo empieza el fade (0–1 normalizado)

    [Header("Escala por tipo")]
    public float scaleNormal = 0.28f;
    public float scaleKill = 0.42f;

    [Header("Offset aleatorio")]
    public float horizontalSpread = 0.3f; // evita que se apilen si hay multihit

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (label == null)
            label = GetComponentInChildren<TextMeshPro>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Muestra el número con el color del tipo de golpe.
    /// Llamado desde HitParticleEmitter.
    /// </summary>
    public void Show(int amount, Color color, bool isKill)
    {
        if (label == null) { Destroy(gameObject); return; }

        // Texto: kills muestran "!" para énfasis
        label.text = isKill ? $"<b>{amount}!</b>" : amount.ToString();
        label.color = color;

        // Escala según importancia
        float scale = isKill ? scaleKill : scaleNormal;
        transform.localScale = Vector3.one * scale;

        // Offset horizontal aleatorio para evitar apilamiento
        Vector3 pos = transform.position;
        pos.x += Random.Range(-horizontalSpread, horizontalSpread);
        transform.position = pos;

        StartCoroutine(Animate());
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ANIMACIÓN
    // ─────────────────────────────────────────────────────────────────────────

    IEnumerator Animate()
    {
        float elapsed = 0f;
        Color baseColor = label.color;

        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / lifetime;

            // Subir con ease-out
            float rise = riseSpeed * (1f - t * 0.6f) * Time.deltaTime;
            transform.position += Vector3.up * rise;

            // Fade en el tramo final
            if (t > fadeStart)
            {
                float fadeT = (t - fadeStart) / (1f - fadeStart);
                label.color = new Color(baseColor.r, baseColor.g, baseColor.b,
                                        Mathf.Lerp(1f, 0f, fadeT));
            }

            yield return null;
        }

        Destroy(gameObject);
    }
}
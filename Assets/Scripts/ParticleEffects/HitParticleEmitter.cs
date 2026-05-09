using System.Collections;
using UnityEngine;

/// <summary>
/// Emite partículas de feedback visual según el ángulo de ataque recibido.
/// Conecta con Unit.ReceiveDamage via el evento onDamageReceived.
///
/// SETUP:
///   1. Añadir este componente al mismo GameObject que Unit.
///   2. Asignar particlePrefab (un GameObject con ParticleSystem configurado).
///   3. El componente se suscribe automáticamente a onDamageReceived.
///
/// Si no usas prefabs de ParticleSystem, también funciona con el
/// método DrawParticles() que crea SpriteRenderers proceduralmente.
/// </summary>
[RequireComponent(typeof(Unit))]
public class HitParticleEmitter : MonoBehaviour
{
    // ── Configuración ─────────────────────────────────────────────────────────

    [Header("Partículas prefab (opcional)")]
    [Tooltip("Prefab con ParticleSystem. Si es null usa partículas procedurales.")]
    public GameObject particlePrefab;

    [Header("Colores por tipo de golpe")]
    public Color colorFrontal = new Color(0.85f, 0.90f, 0.95f, 1f); // blanco azulado
    public Color colorFlank = new Color(1.00f, 0.65f, 0.10f, 1f); // ámbar
    public Color colorBack = new Color(0.90f, 0.20f, 0.20f, 1f); // rojo
    public Color colorKill = new Color(1.00f, 0.85f, 0.10f, 1f); // dorado

    [Header("Cantidades de partículas")]
    public int countFrontal = 4;
    public int countFlank = 7;
    public int countBack = 11;

    [Header("Screen shake")]
    [Tooltip("Sacudir cámara en golpe por la espalda")]
    public bool enableScreenShake = true;
    public float shakeDuration = 0.10f;
    public float shakeMagnitude = 0.08f;

    [Header("Floating damage numbers")]
    public bool showDamageNumbers = true;
    public GameObject damageNumberPrefab; // TextMeshPro o similar

    // ── Estado interno ────────────────────────────────────────────────────────

    private Unit _unit;
    private Camera _cam;
    private Vector3 _camOrigin;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _unit = GetComponent<Unit>();
        _cam = Camera.main;
    }

    void OnEnable()
    {
        _unit.onDamageReceived.AddListener(OnDamageReceived);
    }

    void OnDisable()
    {
        _unit.onDamageReceived.RemoveListener(OnDamageReceived);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HANDLER PRINCIPAL
    // ─────────────────────────────────────────────────────────────────────────

    void OnDamageReceived(float amount, Unit attacker)
    {
        if (attacker == null) return;

        HitType type = ClassifyHit(attacker);
        bool isKill = _unit.IsDead; // Unit ya aplicó el daño antes del evento

        EmitParticles(type, attacker, isKill);

        if (showDamageNumbers)
            SpawnDamageNumber(amount, type, isKill);

        if (enableScreenShake && type == HitType.Back)
            StartCoroutine(ScreenShake());
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CLASIFICACIÓN DEL GOLPE
    // ─────────────────────────────────────────────────────────────────────────

    enum HitType { Frontal, Flank, Back }

    HitType ClassifyHit(Unit attacker)
    {
        // Reutilizamos exactamente la misma lógica que Unit.CalculateAngleBonus
        Vector2 myFacing = _unit.GetFacingDirection();
        Vector2 attackDir = ((Vector2)transform.position
                                - (Vector2)attacker.transform.position).normalized;
        float dot = Vector2.Dot(myFacing, attackDir);

        // dot > 0.5  → ataque por la espalda
        // -0.5..0.5  → flanco
        // < -0.5     → frente
        if (dot > 0.5f) return HitType.Back;
        if (dot > -0.5f) return HitType.Flank;
        return HitType.Frontal;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMISIÓN DE PARTÍCULAS
    // ─────────────────────────────────────────────────────────────────────────

    void EmitParticles(HitType type, Unit attacker, bool isKill)
    {
        // Dirección de impacto (de atacante a víctima, invertida para salir)
        Vector2 impactDir = ((Vector2)transform.position
                            - (Vector2)attacker.transform.position).normalized;

        Color color;
        int count;
        float spreadAngle;
        float speed;

        switch (type)
        {
            case HitType.Back:
                color = colorBack;
                count = countBack;
                spreadAngle = 140f;   // arco amplio hacia arriba
                speed = 4.5f;
                break;
            case HitType.Flank:
                color = colorFlank;
                count = countFlank;
                spreadAngle = 90f;
                speed = 3.5f;
                break;
            default: // Frontal
                color = colorFrontal;
                count = countFrontal;
                spreadAngle = 50f;
                speed = 2.5f;
                break;
        }

        // Kill bonus: añadir estrellas doradas encima
        if (isKill)
        {
            SpawnBurst(transform.position, Vector2.up, colorKill, 6, 140f, 3f);
        }

        if (particlePrefab != null)
            SpawnParticleSystem(type, color);
        else
            SpawnBurst(transform.position, impactDir, color, count, spreadAngle, speed);
    }

    /// <summary>
    /// Partículas procedurales mediante SpriteRenderers animados.
    /// No requiere ParticleSystem ni prefabs.
    /// </summary>
    void SpawnBurst(Vector2 origin, Vector2 baseDir, Color color,
                    int count, float spreadDeg, float speed)
    {
        float halfSpread = spreadDeg * 0.5f;

        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(-halfSpread, halfSpread);
            Vector2 dir = Rotate(baseDir, angle);
            float spd = speed * Random.Range(0.6f, 1.4f);
            float lifetime = Random.Range(0.18f, 0.35f);
            float size = Random.Range(0.06f, 0.14f);

            StartCoroutine(AnimateParticle(origin, dir, spd, color, size, lifetime));
        }
    }

    IEnumerator AnimateParticle(Vector2 start, Vector2 dir, float speed,
                                 Color color, float size, float lifetime)
    {
        // Crear GO con SpriteRenderer (cuadrado blanco pixel)
        var go = new GameObject("HitParticle");
        go.transform.position = start;
        go.transform.localScale = Vector3.one * size;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetPixelSprite();
        sr.color = color;
        sr.sortingOrder = 30; // encima de unidades

        float elapsed = 0f;
        Vector2 pos = start;
        float gravity = -6f; // caída natural

        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / lifetime;

            // Movimiento con deceleration + gravedad
            float eased = 1f - t * t;
            pos += dir * (speed * eased * Time.deltaTime);
            pos.y += gravity * t * Time.deltaTime;

            go.transform.position = pos;

            // Fade out en el último 40%
            float alpha = t > 0.6f ? Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f) : 1f;
            sr.color = new Color(color.r, color.g, color.b, alpha);

            yield return null;
        }

        Destroy(go);
    }

    /// <summary>
    /// Alternativa: configurar y disparar un ParticleSystem prefab.
    /// Útil si ya tienes un sistema de partículas con texturas propias.
    /// </summary>
    void SpawnParticleSystem(HitType type, Color color)
    {
        var instance = Instantiate(particlePrefab, transform.position, Quaternion.identity);
        var ps = instance.GetComponent<ParticleSystem>();
        if (ps == null) return;

        // Sobreescribir color principal
        var main = ps.main;
        main.startColor = color;

        // Ajustar cantidad según tipo
        var emission = ps.emission;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, type == HitType.Back   ? countBack  :
                                         type == HitType.Flank  ? countFlank :
                                                                   countFrontal)
        });

        ps.Play();
        Destroy(instance, ps.main.duration + ps.main.startLifetime.constantMax);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  FLOATING DAMAGE NUMBERS
    // ─────────────────────────────────────────────────────────────────────────

    void SpawnDamageNumber(float amount, HitType type, bool isKill)
    {
        if (damageNumberPrefab != null)
        {
            // Usar prefab con TextMeshPro — asigna color y texto externamente
            var go = Instantiate(damageNumberPrefab,
                                  (Vector2)transform.position + Vector2.up * 0.5f,
                                  Quaternion.identity);
            var dnd = go.GetComponent<DamageNumberDisplay>();
            if (dnd != null)
            {
                Color numColor = type == HitType.Back ? colorBack :
                                 type == HitType.Flank ? colorFlank :
                                                         colorFrontal;
                dnd.Show(Mathf.RoundToInt(amount), numColor, isKill);
            }
        }
        else
        {
            // Fallback procedural sin prefab
            StartCoroutine(FloatingText(Mathf.RoundToInt(amount), type, isKill));
        }
    }

    IEnumerator FloatingText(int amount, HitType type, bool isKill)
    {
        // Requiere TextMeshPro en el proyecto.
        // Si no lo tienes, usa este coroutine como guía y adapta.
        // Se omite la implementación concreta para no añadir dependencias
        // que quizás no tengas configuradas.
        yield break;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SCREEN SHAKE
    // ─────────────────────────────────────────────────────────────────────────

    IEnumerator ScreenShake()
    {
        if (_cam == null) yield break;
        _camOrigin = _cam.transform.localPosition;

        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            elapsed += Time.unscaledDeltaTime; // funciona en pausa táctica
            float progress = elapsed / shakeDuration;

            // Sacudida que se amortigua al final
            float strength = shakeMagnitude * (1f - progress);
            _cam.transform.localPosition = _camOrigin + (Vector3)Random.insideUnitCircle * strength;

            yield return null;
        }
        _cam.transform.localPosition = _camOrigin;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UTILIDADES
    // ─────────────────────────────────────────────────────────────────────────

    static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    static Sprite _pixelSprite;
    static Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
                tex.SetPixel(x, y, Color.white);
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        return _pixelSprite;
    }
}
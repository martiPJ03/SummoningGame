using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Detecta contactos físicos (collider NO-trigger) con unidades del bando
/// contrario y calcula un multiplicador de velocidad basado en el peso
/// relativo de cada contacto. Múltiples contactos se acumulan con
/// rendimientos decrecientes (no se suman linealmente).
///
/// SETUP:
///   · Añadir a la unidad junto a Unit.
///   · Requiere un Rigidbody2D (BodyType = Kinematic) — solo para que
///     OnCollisionEnter2D/Exit2D se disparen; no mueve nada.
///   · El Collider2D no-trigger de bloqueo físico debe estar en el MISMO
///     GameObject (o pasar su referencia si está en un hijo).
/// </summary>
[RequireComponent(typeof(Unit))]
public class UnitWeightCollision : MonoBehaviour
{
    private Unit _unit;
    private readonly List<Unit> _contacts = new List<Unit>();

    /// <summary>Multiplicador [0..1] a aplicar sobre moveSpeed este frame.</summary>
    public float SlowMultiplier { get; private set; } = 1f;

    void Awake()
    {
        _unit = GetComponent<Unit>();
    }

    void FixedUpdate()
    {
        RecalculateSlow();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        TryRegister(collision.collider);
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        var other = collision.collider.GetComponentInParent<Unit>();
        if (other != null) _contacts.Remove(other);
    }

    void TryRegister(Collider2D col)
    {
        var other = col.GetComponentInParent<Unit>();
        if (other == null || other == _unit) return;
        if (other.side == _unit.side) return;
        if (other.IsDead) return;
        if (!_contacts.Contains(other)) _contacts.Add(other);
    }

    void RecalculateSlow()
    {
        // Limpiar contactos muertos/destruidos
        _contacts.RemoveAll(u => u == null || u.IsDead);

        float remaining = 1f;
        float ownWeight = Mathf.Max(0.01f, _unit.stats.weight);

        foreach (var enemy in _contacts)
        {
            float enemyWeight = Mathf.Max(0.01f, enemy.stats.weight);
            float contribution = (enemyWeight / (ownWeight + enemyWeight))
                                  * _unit.stats.collisionSlowFactorMax;

            remaining *= (1f - contribution);
        }

        SlowMultiplier = Mathf.Clamp(remaining, 0.05f, 1f);
    }
}
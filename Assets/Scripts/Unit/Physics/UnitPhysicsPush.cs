using UnityEngine;

/// <summary>
/// Afegeix un CircleCollider2D de física (no-trigger) per simular empenta entre
/// unitats de bàndols oposats. Les unitats del mateix bàndol NO s'empenten.
///
/// SETUP REQUERIT (una sola vegada a Unity):
///   Edit → Project Settings → Physics 2D → Layer Collision Matrix
///   · "PlayerUnit" vs "PlayerUnit" → DESACTIVAT  (aliats no s'empenten)
///   · "EnemyUnit"  vs "EnemyUnit"  → DESACTIVAT  (enemics no s'empenten)
///   · "PlayerUnit" vs "EnemyUnit"  → ACTIVAT     (bàndols oposats s'empenten)
///
/// Aquest component s'ha d'afegir al mateix GameObject que Unit i UnitLayerSetup.
/// UnitLayerSetup ja assigna la layer correcta; aquest script només afegeix el
/// collider físic i configura el Rigidbody2D necessari.
///
/// NOTA: El BoxCollider2D existent als prefabs ha de continuar sent IsTrigger = true
/// perquè SelectionManager faci OverlapCircle. Aquest script afegeix un collider
/// SEPARAT exclusivament per a física.
/// </summary>
[RequireComponent(typeof(Unit))]
[RequireComponent(typeof(Rigidbody2D))]
public class UnitPhysicsPush : MonoBehaviour
{
    [Header("Collider de física (push)")]
    [Tooltip("Radi del collider circular de física. Ajustar perquè coincideixi " +
             "aproximadament amb la meitat de l'amplada del sprite.")]
    public float pushRadius = 0.3f;

    [Tooltip("Força de separació aplicada quan dues unitats es superposen.")]
    public float pushForce = 2.5f;

    private Rigidbody2D _rb;
    private Unit _unit;
    private CircleCollider2D _pushCollider;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _unit = GetComponent<Unit>();
        SetupRigidbody();
        SetupPushCollider();

        // Desactivar el collider de push quan la unitat mor
        _unit.onDeath.AddListener(_ =>
        {
            if (_pushCollider != null)
                _pushCollider.enabled = false;
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SETUP
    // ─────────────────────────────────────────────────────────────────────────

    void SetupRigidbody()
    {
        _rb = GetComponent<Rigidbody2D>();

        // Kinematic: el NavMeshAgent controla el moviment, no la física.
        // Però cal que NO sigui kinematic perquè rebi forces de separació.
        // Solució: Dynamic amb constraints per evitar rotació i moviment Z.
        _rb.bodyType = RigidbodyType2D.Dynamic;
        _rb.gravityScale = 0f;
        _rb.linearDamping = 10f;   // amortir ràpidament per no lliscar
        _rb.angularDamping = 10f;
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // Massa lleugera perquè el NavMesh pugui corregir la posició fàcilment
        _rb.mass = 0.5f;
    }

    void SetupPushCollider()
    {
        _pushCollider = gameObject.AddComponent<CircleCollider2D>();
        _pushCollider.radius = pushRadius;
        _pushCollider.isTrigger = false;  // ← físic, no trigger
        _pushCollider.offset = Vector2.zero;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PHYSICS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Quan dos colliders físics es toquen Unity aplica resolució de contacte
    /// automàticament. Però com el NavMeshAgent sobreescriu la posició cada frame,
    /// necessitem aplicar una força de separació addicional perquè el resultat
    /// sigui visible.
    /// </summary>
    void OnCollisionStay2D(Collision2D collision)
    {
        if (_unit.IsDead) return;

        // Només reaccionem a col·lisions amb unitats del bàndol contrari
        Unit other = collision.gameObject.GetComponent<Unit>();
        if (other == null || other.side == _unit.side) return;

        // Direcció de separació
        Vector2 pushDir = ((Vector2)transform.position - (Vector2)other.transform.position);
        if (pushDir.sqrMagnitude < 0.0001f)
            pushDir = Random.insideUnitCircle.normalized;  // evitar divisió per zero
        else
            pushDir = pushDir.normalized;

        // Força de separació proporcional al solapament
        float overlap = pushRadius - collision.contacts[0].separation;
        overlap = Mathf.Max(0f, overlap);

        _rb.AddForce(pushDir * pushForce * overlap, ForceMode2D.Force);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GIZMO
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, pushRadius);
    }
#endif
}
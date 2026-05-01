using UnityEngine;

/// <summary>
/// Asigna la Physics Layer correcta a cada unidad según su bando.
/// 
/// SETUP REQUERIDO EN UNITY (una sola vez):
///   Edit → Project Settings → Physics 2D → Layer Collision Matrix
///   · "PlayerUnit"  vs "PlayerUnit"  → DESACTIVADO  (aliados se ignoran)
///   · "EnemyUnit"   vs "EnemyUnit"   → DESACTIVADO  (enemigos se ignoran)
///   · "PlayerUnit"  vs "EnemyUnit"   → ACTIVADO     (colisión entre bandos)
///
/// Añadir este componente al mismo GameObject que Unit,
/// o llamar a Apply() desde Unit.Awake() después de asignar 'side'.
/// </summary>
[RequireComponent(typeof(Unit))]
public class UnitLayerSetup : MonoBehaviour
{
    // Nombres de layer — deben coincidir exactamente con los creados en
    // Edit > Project Settings > Tags and Layers
    private const string layerPlayer = "PlayerUnit";
    private const string layerEnemy = "EnemyUnit";

    public void Apply(UnitSide side)
    {
        int layer = LayerMask.NameToLayer(side == UnitSide.Player ? layerPlayer : layerEnemy);

        SetLayerRecursive(gameObject, layer);
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
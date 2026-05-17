using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;

public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance { get; private set; }

    [Header("Estado del Combate")]
    public bool isGameOver = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// Llamado por PlayerUnit cuando muere.
    /// </summary>
    public void OnPlayerUnitDied(PlayerUnit unit)
    {
        if (isGameOver) return;

        Debug.Log($"¡Derrota! El jugador {unit.unitName} ha muerto.");
        EndCombat(false);
    }

    /// <summary>
    /// Comprueba si quedan enemigos en la escena. 
    /// Se puede llamar desde el evento onDeath de cualquier EnemyUnit.
    /// </summary>
    public void CheckVictoryCondition()
    {
        if (isGameOver) return;

        // Buscamos todas las unidades y filtramos por bando enemigo
        var enemies = FindObjectsByType<Unit>(FindObjectsSortMode.None)
                        .Where(u => u.side == UnitSide.Enemy && !u.IsDead);

        if (!enemies.Any())
        {
            Debug.Log("¡Victoria! Todos los enemigos han sido eliminados.");
            EndCombat(true);
        }
    }

    private void EndCombat(bool victory)
    {
        isGameOver = true;

        if (victory)
        {
            // Lógica de victoria (ej: mostrar panel, cargar siguiente nivel)
        }
        else
        {
            // Lógica de Game Over (ej: mostrar botón de reiniciar)
            TriggerGameOverUI();
        }
    }

    private void TriggerGameOverUI()
    {
        // Aquí llamarías a tu script de UI
        Debug.Log("Mostrando pantalla de GAME OVER...");
    }

    // Método de utilidad para reiniciar la batalla
    public void RestartCombat()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
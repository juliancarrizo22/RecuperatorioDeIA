using UnityEngine;

public class InterestObject : MonoBehaviour
{
    [SerializeField] private int maxHealth = 2;
    public int CurrentHealth { get; private set; }

    private void Awake()
    {
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(int amount)
    {
        CurrentHealth -= amount;
        if (CurrentHealth < 0)
            CurrentHealth = 0;
    }
}

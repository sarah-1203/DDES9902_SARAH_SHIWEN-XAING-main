using UnityEngine;

/// <summary>Reports watering-can contact with the planter's forgiving trigger zone.</summary>
public class PotWateringCollisionRelay : MonoBehaviour
{
    public PlantingFlowController controller;

    private void OnTriggerEnter(Collider other)
    {
        if (controller != null) controller.OnWateringCanEnteredPot(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (controller != null) controller.OnWateringCanEnteredPot(other);
    }
}

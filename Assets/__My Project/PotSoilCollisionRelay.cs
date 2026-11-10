using UnityEngine;

/// <summary>Forwards soil entering the planter's collision zone to the flow controller.</summary>
public class PotSoilCollisionRelay : MonoBehaviour
{
    public PlantingFlowController controller;

    private void OnTriggerEnter(Collider other)
    {
        if (controller != null) controller.OnSoilEnteredPot(other);
    }
}

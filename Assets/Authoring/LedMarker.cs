using UnityEngine;

public enum LedChannel { Processor, Radar, Network, User }
public class LedMarker : MonoBehaviour
{
    public LedChannel channel;
    public float radius = 0.02f;   // object units, assumes unit root scale 1
    void OnDrawGizmos() { Gizmos.DrawWireSphere(transform.position, radius); }
}

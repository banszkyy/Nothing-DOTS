using Unity.Entities;

struct PausableTime : IComponentData
{
    public bool IsPaused;
    public double ElapsedTime;
    public float DeltaTime;
}

using Unity.Entities;
using Unity.Burst;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct GameOutcomeSystemServer : ISystem
{
    double _refreshAt;

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        double now = SystemAPI.Time.ElapsedTime;
        if (now < _refreshAt) return;
        _refreshAt = now + 1d;

        int nonlosers = 0;
        int losers = 0;
        foreach (var (player, playerE) in
            SystemAPI.Query<RefRW<Player>>()
            .WithEntityAccess())
        {
            if (SystemAPI.TryGetComponent(playerE, out RealPlayer playerR) && playerR.ConnectionState == PlayerConnectionState.Server) continue;

            bool ok = false;
            foreach (var team in
                SystemAPI.Query<RefRO<UnitTeam>>()
                .WithAll<CoreComputer>())
            {
                if (team.ValueRO.Team != player.ValueRO.Team) continue;

                ok = true;
                break;
            }

            if (!ok)
            {
                player.ValueRW.Outcome = GameOutcome.Lost;
                losers++;
            }
            else
            {
                player.ValueRW.Outcome = GameOutcome.None;
                nonlosers++;
            }
        }

        if (nonlosers == 1 && losers > 0)
        {
            foreach (var player in
                SystemAPI.Query<RefRW<Player>>())
            {
                if (player.ValueRO.Outcome == GameOutcome.Lost) continue;
                player.ValueRW.Outcome = GameOutcome.Won;
                break;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Utilities;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

partial class DebugHost
{
    protected override AttachResponse HandleAttachRequest(AttachArguments arguments)
    {
        Log.Trace($"[Handler] Attach");

        Entity entity;
        Guid guid;
        int playerConnectionId;
        Entity playerEntity;
        int playerTeam;

        {
            string guidValue = arguments.ConfigurationProperties.GetValueAsString("token");
            if (string.IsNullOrEmpty(guidValue))
            {
                throw new ProtocolException("Attach failed because launch configuration did not specify 'token'.");
            }

            if (!Guid.TryParse(guidValue, out guid))
            {
                throw new ProtocolException("Attach failed because couldn't parse the token.");
            }
        }

        EntityManager e = ConnectionManager.ServerOrDefaultWorld.EntityManager;

        {
            using var q = e.CreateEntityQuery(typeof(Player), typeof(RealPlayer));
            using var playerEntities = q.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < playerEntities.Length; i++)
            {
                var player = e.GetComponentData<Player>(playerEntities[i]);
                var playerR = e.GetComponentData<RealPlayer>(playerEntities[i]);
                if (player.Guid != guid) continue;

                playerConnectionId = playerR.ConnectionId;
                playerEntity = playerEntities[i];
                playerTeam = player.Team;

                goto ok;
            }

            throw new ProtocolException("Attach failed because the token is invalid.");
        ok:;
        }

        {
            string entityId = arguments.ConfigurationProperties.GetValueAsString("entity");
            if (string.IsNullOrEmpty(entityId))
            {
                string ghostId = arguments.ConfigurationProperties.GetValueAsString("ghost");
                if (string.IsNullOrEmpty(ghostId))
                {
                    throw new ProtocolException("Attach failed because launch configuration did not specify 'entity' or 'ghost'.");
                }

                if (!ExtensionHostUtils.TryParseGhost(ghostId, out var ghost))
                {
                    throw new ProtocolException($"Attach failed because the ghost is invalid.");
                }

                using var q = e.CreateEntityQuery(typeof(Processor), typeof(GhostInstance));
                using var entities = q.ToEntityArray(Allocator.Temp);

                for (int i = 0; i < entities.Length; i++)
                {
                    if (ghost.Equals(e.GetComponentData<GhostInstance>(entities[i])))
                    {
                        entity = entities[i];
                        goto ok;
                    }
                }

                throw new ProtocolException($"Attach failed because the ghost {ghost} not found.");

            ok:;
            }
            else
            {
                if (!ExtensionHostUtils.TryParseEntity(entityId, out entity))
                {
                    throw new ProtocolException($"Attach failed because the entity is invalid.");
                }
            }
        }

        if (!e.Exists(entity))
        {
            throw new ProtocolException($"Attach failed because entity {entity} doesn't exist.");
        }

        if (!e.HasComponent<Processor>(entity))
        {
            throw new ProtocolException($"Attach failed because entity {entity} doesn't have a {typeof(Processor)} component.");
        }

        Log.Trace($"Disposing previous session");
        DisposeSession();

        NoDebug = false;

        Processor _processor = e.GetComponentData<Processor>(entity);

        if (_processor.DebugContext.IsBeingDebugged)
        {
            throw new ProtocolException($"Attach failed because entity {entity} is currently being debugged by someone else.");
        }

        var entityTeam = !e.HasComponent<UnitTeam>(entity) ? -1 : e.GetComponentData<UnitTeam>(entity).Team;

        if (entityTeam != playerTeam)
        {
            throw new ProtocolException($"Attach failed because you cannot debug your opponent's units.");
        }

        Log.Info($"Debug session started: {{ entity: {entity}, token: {guid}, connection: {playerConnectionId} }}");

        _entity = entity;
        _playerGuid = guid;
        _playerConnectionId = playerConnectionId;
        _playerEntity = playerEntity;
        _originalFile = _processor.SourceFile;
        _originalSource = _processor.Source;
        _unitLogPosition = 0;
        _processor.DebugContext = new ProcessorJob.DebugContext()
        {
            IsBeingDebugged = true,
            Breakpoints = new FixedList128Bytes<ushort>(),
        };

        e.SetComponentData(_entity, _processor);

        unsafe
        {
            List<UnitModule> res = new();

            MappedMemory m = default;

            res.Add(new("GPS", new UnitModuleField[]
            {
                new("forward", "float3", &m.GPS.Forward, &m),
                new("position", "float3", &m.GPS.Position, &m),
            }));

            Protocol.SendEvent(new UnitModulesEvent(res.ToArray()));
        }

        return new AttachResponse();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.NetCode;
using Unity.Transforms;
using BinaryReader = Unity.Entities.Serialization.BinaryReader;
using BinaryWriter = Unity.Entities.Serialization.BinaryWriter;

static class SaveManager
{
    public delegate void BasicSerializer(BinaryWriter writer, Entity entity, EntityManager entityManager);
    public delegate void BasicDeserializer(BinaryReader reader, Entity entity, EntityManager entityManager);

    public delegate void EntityReferencePopulatorSimple(Entity entity, EntityManager entityManager, EntityReferenceResolver entityReferenceResolver);
    public delegate void EntityReferencePopulator<T>(ref T component, EntityReferenceResolver entityReferenceResolver);
    public delegate Entity EntityReferenceResolver(Entity entity);

    public delegate void GhostReferencePopulatorSimple(Entity entity, EntityManager entityManager, GhostReferenceResolver ghostReferenceResolver);
    public delegate void GhostReferencePopulator<T>(ref T component, GhostReferenceResolver ghostReferenceResolver);
    public delegate SpawnedGhost GhostReferenceResolver(Entity entity);

    public delegate void ComponentSerializer<T>(BinaryWriter writer, T component);
    public delegate void ComponentDeserializer<T>(BinaryReader reader, ref T component);
    public delegate T ComponentDeserializerSimple<T>(BinaryReader reader);

    public delegate void DynamicBufferSerializer<T>(BinaryWriter writer, DynamicBuffer<T> buffer) where T : unmanaged, IBufferElementData;
    public delegate void DynamicBufferDeserializer<T>(BinaryReader reader, DynamicBuffer<T> buffer) where T : unmanaged, IBufferElementData;

    readonly unsafe struct TypeSerializer
    {
        public readonly ComponentType Type;
        public readonly BasicSerializer Serializer;
        public readonly BasicDeserializer Deserializer;
        public readonly EntityReferencePopulatorSimple? EntityReferencePopulator;
        public readonly GhostReferencePopulatorSimple? GhostReferencePopulator;

        public TypeSerializer(ComponentType type, BasicSerializer serializer, BasicDeserializer deserializer, EntityReferencePopulatorSimple? entityReferencePopulator = null, GhostReferencePopulatorSimple? ghostReferencePopulator = null)
        {
            Type = type;
            Serializer = serializer;
            Deserializer = deserializer;
            EntityReferencePopulator = entityReferencePopulator;
            GhostReferencePopulator = ghostReferencePopulator;
        }

        public static TypeSerializer Simple<T>(BasicSerializer serializer, BasicDeserializer deserializer) => new(typeof(T), serializer, deserializer);

        public static TypeSerializer ForDynamicBuffer<T>(DynamicBufferSerializer<T> serializer, DynamicBufferDeserializer<T> deserializer, EntityReferencePopulator<T>? entityReferencePopulator = null, GhostReferencePopulator<T>? ghostReferencePopulator = null) where T : unmanaged, IBufferElementData
        {
            if (((ComponentType)typeof(T)).IsZeroSized) throw new InvalidOperationException($"{typeof(T)} is zero sized");

            return new TypeSerializer(
                typeof(T),
                (writer, entity, entityManager) => serializer(writer, entityManager.GetBuffer<T>(entity)),
                (reader, entity, entityManager) => deserializer(reader, entityManager.GetBuffer<T>(entity)),
                entityReferencePopulator is null ? null : (Entity entity, EntityManager entityManager, EntityReferenceResolver entityReferenceResolver) =>
                {
                    var buffer = entityManager.GetBuffer<T>(entity);
                    var ptr = (T*)buffer.GetUnsafePtr();
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        entityReferencePopulator(ref ptr[i], entityReferenceResolver);
                    }
                },
                ghostReferencePopulator is null ? null : (Entity entity, EntityManager entityManager, GhostReferenceResolver ghostReferenceResolver) =>
                {
                    var buffer = entityManager.GetBuffer<T>(entity);
                    var ptr = (T*)buffer.GetUnsafePtr();
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        ghostReferencePopulator(ref ptr[i], ghostReferenceResolver);
                    }
                }
            );
        }

        public static TypeSerializer ForDynamicBufferItem<T>(BinaryWriterExtensions.ItemSerializer<T> serializer, BinaryReaderExtensions.ItemDeserializer<T> deserializer, EntityReferencePopulator<T>? entityReferencePopulator = null, GhostReferencePopulator<T>? ghostReferencePopulator = null) where T : unmanaged, IBufferElementData
        {
            if (((ComponentType)typeof(T)).IsZeroSized) throw new InvalidOperationException($"{typeof(T)} is zero sized");

            return new TypeSerializer(
                typeof(T),
                (writer, entity, entityManager) => writer.Write(entityManager.GetBuffer<T>(entity), serializer),
                (reader, entity, entityManager) => reader.ReadDynamicBuffer<T>(entityManager.GetBuffer<T>(entity), deserializer),
                entityReferencePopulator is null ? null : (Entity entity, EntityManager entityManager, EntityReferenceResolver entityReferenceResolver) =>
                {
                    var buffer = entityManager.GetBuffer<T>(entity);
                    var ptr = (T*)buffer.GetUnsafePtr();
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        entityReferencePopulator(ref ptr[i], entityReferenceResolver);
                    }
                },
                ghostReferencePopulator is null ? null : (Entity entity, EntityManager entityManager, GhostReferenceResolver ghostReferenceResolver) =>
                {
                    var buffer = entityManager.GetBuffer<T>(entity);
                    var ptr = (T*)buffer.GetUnsafePtr();
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        ghostReferencePopulator(ref ptr[i], ghostReferenceResolver);
                    }
                }
            );
        }

        public static TypeSerializer ForComponentSimple<T>(ComponentSerializer<T> serializer, ComponentDeserializerSimple<T> deserializer, EntityReferencePopulator<T>? entityReferencePopulator = null, GhostReferencePopulator<T>? ghostReferencePopulator = null) where T : unmanaged, IComponentData
        {
            if (((ComponentType)typeof(T)).IsZeroSized)
            {
                return new TypeSerializer(
                    typeof(T),
                    (writer, entity, entityManager) => serializer(writer, default),
                    (reader, entity, entityManager) => deserializer(reader),
                    entityReferencePopulator is null ? null : (Entity entity, EntityManager entityManager, EntityReferenceResolver entityReferenceResolver) =>
                    {
                        T componentData = entityManager.GetComponentData<T>(entity);
                        entityReferencePopulator(ref componentData, entityReferenceResolver);
                        entityManager.SetComponentData<T>(entity, componentData);
                    },
                    ghostReferencePopulator is null ? null : (Entity entity, EntityManager entityManager, GhostReferenceResolver ghostReferenceResolver) =>
                    {
                        T componentData = entityManager.GetComponentData<T>(entity);
                        ghostReferencePopulator(ref componentData, ghostReferenceResolver);
                        entityManager.SetComponentData<T>(entity, componentData);
                    }
                );
            }
            else
            {
                return new TypeSerializer(
                    typeof(T),
                    (writer, entity, entityManager) => serializer(writer, entityManager.GetComponentData<T>(entity)),
                    (reader, entity, entityManager) => entityManager.SetComponentData(entity, deserializer(reader)),
                    entityReferencePopulator is null ? null : (Entity entity, EntityManager entityManager, EntityReferenceResolver entityReferenceResolver) =>
                    {
                        T componentData = entityManager.GetComponentData<T>(entity);
                        entityReferencePopulator(ref componentData, entityReferenceResolver);
                        entityManager.SetComponentData<T>(entity, componentData);
                    },
                    ghostReferencePopulator is null ? null : (Entity entity, EntityManager entityManager, GhostReferenceResolver ghostReferenceResolver) =>
                    {
                        T componentData = entityManager.GetComponentData<T>(entity);
                        ghostReferencePopulator(ref componentData, ghostReferenceResolver);
                        entityManager.SetComponentData<T>(entity, componentData);
                    }
                );
            }
        }

        public static TypeSerializer ForComponent<T>(ComponentSerializer<T> serializer, ComponentDeserializer<T> deserializer, EntityReferencePopulator<T>? entityReferencePopulator = null, GhostReferencePopulator<T>? ghostReferencePopulator = null) where T : unmanaged, IComponentData
        {
            if (((ComponentType)typeof(T)).IsZeroSized) throw new UnreachableException();
            return new TypeSerializer(
                typeof(T),
                (writer, entity, entityManager) =>
                {
                    serializer(writer, entityManager.GetComponentData<T>(entity));
                },
                (reader, entity, entityManager) =>
                {
                    T v = entityManager.GetComponentData<T>(entity);
                    deserializer(reader, ref v);
                    entityManager.SetComponentData(entity, v);
                },
                entityReferencePopulator is null ? null : (Entity entity, EntityManager entityManager, EntityReferenceResolver entityReferenceResolver) =>
                {
                    T componentData = entityManager.GetComponentData<T>(entity);
                    entityReferencePopulator(ref componentData, entityReferenceResolver);
                    entityManager.SetComponentData<T>(entity, componentData);
                },
                ghostReferencePopulator is null ? null : (Entity entity, EntityManager entityManager, GhostReferenceResolver ghostReferenceResolver) =>
                {
                    T componentData = entityManager.GetComponentData<T>(entity);
                    ghostReferencePopulator(ref componentData, ghostReferenceResolver);
                    entityManager.SetComponentData<T>(entity, componentData);
                }
            );
        }
    }

    readonly struct PrefabIdSerializer
    {
        public readonly PrefabIdentifier Is;
        public readonly BasicSerializer Serializer;
        public readonly BasicDeserializer Deserializer;

        public delegate bool PrefabIdentifier(ComponentType componentType);

        public delegate void BasicSerializer(BinaryWriter writer, Entity entity, EntityManager entityManager);
        public delegate void ComponentSerializer<T>(BinaryWriter writer, T component);
        public delegate Entity BasicDeserializer(BinaryReader reader, EntityManager entityManager);

        public PrefabIdSerializer(PrefabIdentifier prefabIdentifier, BasicSerializer serializer, BasicDeserializer deserializer)
        {
            Is = prefabIdentifier;
            Serializer = serializer;
            Deserializer = deserializer;
        }

        public static PrefabIdSerializer Simple(PrefabIdentifier prefabIdentifier, BasicSerializer serializer, BasicDeserializer deserializer) => new(prefabIdentifier, serializer, deserializer);

        public static PrefabIdSerializer ForComponent<T>(ComponentSerializer<T> serializer, BasicDeserializer deserializer) where T : unmanaged, IComponentData
        {
            if (((ComponentType)typeof(T)).IsZeroSized)
            {
                return new(
                    static (componentType) => componentType.Equals((ComponentType)typeof(T)),
                    (writer, entity, entityManager) => serializer(writer, default),
                    deserializer
                );
            }
            else
            {
                return new(
                    static (componentType) => componentType.Equals((ComponentType)typeof(T)),
                    (writer, entity, entityManager) => serializer(writer, entityManager.GetComponentData<T>(entity)),
                    deserializer
                );
            }
        }
    }

    static DynamicBuffer<TBufferedItem> GetSingletonBuffer<TSingleton, TBufferedItem>(EntityManager entityManager, bool isReadOnly = false)
        where TSingleton : unmanaged, IComponentData
        where TBufferedItem : unmanaged, IBufferElementData
    {
        using EntityQuery query = entityManager.CreateEntityQuery(typeof(TSingleton));
        Entity entity = query.GetSingletonEntity();
        return entityManager.GetBuffer<TBufferedItem>(entity, isReadOnly);
    }

    static TSingleton GetSingleton<TSingleton>(EntityManager entityManager)
        where TSingleton : unmanaged, IComponentData
    {
        using EntityQuery query = entityManager.CreateEntityQuery(typeof(TSingleton));
        return query.GetSingleton<TSingleton>();
    }

    static List<TypeSerializer> GetSerializers(EntityManager entityManager)
    {
        DynamicBuffer<BufferedBuilding> buildings = GetSingletonBuffer<BuildingDatabase, BufferedBuilding>(entityManager, false);
        DynamicBuffer<BufferedUnit> units = GetSingletonBuffer<UnitDatabase, BufferedUnit>(entityManager, false);
        DynamicBuffer<BufferedProjectile> projectiles = GetSingletonBuffer<ProjectileDatabase, BufferedProjectile>(entityManager, false);
        PrefabDatabase prefabs = GetSingleton<PrefabDatabase>(entityManager);
        NativeArray<Research>.ReadOnly researches;
        {
            EntityQuery q = entityManager.CreateEntityQuery(typeof(Research));
            researches = q.ToComponentDataArray<Research>(Allocator.Temp).AsReadOnly();
            q.Dispose();
        }

        return new()
        {
            TypeSerializer.ForComponentSimple<LocalTransform>(
                (writer, v) =>
                {
                    writer.Write(v.Position);
                    writer.Write(v.Rotation);
                },
                (reader) =>
                {
                    return LocalTransform.FromPositionRotation(reader.ReadFloat3(), reader.ReadQuaternion());
                }
            ),
            TypeSerializer.ForComponentSimple<BuildingPrefabInstance>(
                (writer, v) =>
                {
                    writer.Write(v.Index);
                },
                (reader) =>
                {
                    return new BuildingPrefabInstance()
                    {
                        Index = reader.ReadInt(),
                    };
                }
            ),
            TypeSerializer.ForComponentSimple<UnitPrefabInstance>(
                (writer, v) =>
                {
                    writer.Write(v.Index);
                },
                (reader) =>
                {
                    return new UnitPrefabInstance()
                    {
                        Index = reader.ReadInt(),
                    };
                }
            ),
            TypeSerializer.ForComponentSimple<UnitTeam>(
                (writer, v) =>
                {
                    writer.Write(v.Team);
                },
                (reader) =>
                {
                    return new UnitTeam()
                    {
                        Team = reader.ReadInt(),
                    };
                }
            ),
            TypeSerializer.ForComponentSimple<Vehicle>(
                (writer, v) =>
                {
                    writer.Write(v.Input);
                    writer.Write(v.Speed);
                },
                (reader) =>
                {
                    return new Vehicle()
                    {
                        Input = reader.ReadFloat2(),
                        Speed = reader.ReadFloat(),
                    };
                }
            ),
            TypeSerializer.ForComponent<Rigidbody>(
                (writer, v) =>
                {
                    writer.Write(v.Velocity);
                    writer.Write(v.IsEnabled);
                },
                (BinaryReader reader, ref Rigidbody v) =>
                {
                    v.Velocity = reader.ReadFloat3();
                    v.IsEnabled = reader.ReadBool();
                }
            ),
            TypeSerializer.ForComponentSimple<Facility>(
                (writer, v) =>
                {
                    writer.Write(v.Current.Name);
                    writer.Write(v.CurrentProgress);
                },
                (reader) =>
                {
                    FixedString64Bytes name = reader.ReadFixedString64();
                    int i = researches.IndexOf(v => v.Name == name);
                    if (i == -1 && name != default)
                    {
                        Debug.LogError($"Research `{name}` not found");
                    }
                    return new Facility()
                    {
                        Current = i == -1 ? default : new BufferedResearch()
                        {
                            Name = researches[i].Name,
                            ResearchTime = researches[i].ResearchTime,
                            Hash = researches[i].Hash,
                        },
                        CurrentProgress = reader.ReadFloat(),
                    };
                }
            ),
            TypeSerializer.ForComponentSimple<Factory>(
                (writer, v) =>
                {
                    writer.Write(v.Current.Name);
                    writer.Write(v.CurrentProgress);
                    writer.Write(v.TotalProgress);
                },
                (reader) =>
                {
                    FixedString32Bytes name = reader.ReadFixedString32();
                    int i = units.IndexOf(v => v.Name == name);
                    if (i == -1)
                    {
                        Debug.LogError($"Unit not found");
                    }
                    return new Factory()
                    {
                        Current = i == -1 ? default : new BufferedProducingUnit()
                        {
                            Name = units[i].Name,
                            Prefab = units[i].Prefab,
                            ProductionTime = units[i].ProductionTime,
                        },
                        CurrentProgress = reader.ReadFloat(),
                        TotalProgress = reader.ReadFloat(),
                    };
                }
            ),
            TypeSerializer.ForComponent<Extractor>(
                (writer, v) =>
                {
                    writer.Write(v.ExtractProgress);
                },
                (BinaryReader reader, ref Extractor v) =>
                {
                    v.ExtractProgress = reader.ReadFloat();
                }
            ),
            TypeSerializer.ForComponent<Transporter>(
                (writer, v) =>
                {
                    writer.Write(v.LoadProgress);
                    writer.Write(v.CurrentLoad);
                },
                (BinaryReader reader, ref Transporter v) =>
                {
                    v.LoadProgress = reader.ReadFloat();
                    v.CurrentLoad = reader.ReadInt();
                }
            ),
            TypeSerializer.ForComponent<Damageable>(
                (writer, v) =>
                {
                    writer.Write(v.Health);
                },
                (BinaryReader reader, ref Damageable v) =>
                {
                    v.Health = reader.ReadFloat();
                }
            ),
            TypeSerializer.ForComponent<Resource>(
                (writer, v) =>
                {
                    writer.Write(v.Amount);
                },
                (BinaryReader reader, ref Resource v) =>
                {
                    v.Amount = reader.ReadInt();
                }
            ),
            TypeSerializer.ForComponentSimple<BuildingPlaceholder>(
                (writer, v) =>
                {
                    int i = buildings.IndexOf(w => w.Prefab == v.BuildingPrefab);
                    if (i == -1) Debug.LogError($"Building prefab `{v.BuildingPrefab}` not found");
                    writer.Write(i);
                    writer.Write(v.CurrentProgress);
                    writer.Write(v.TotalProgress);
                },
                (reader) =>
                {
                    int i = reader.ReadInt();
                    if (i == -1) Debug.LogError($"Building prefab not found");
                    return new BuildingPlaceholder()
                    {
                        BuildingPrefab = buildings[i].Prefab,
                        CurrentProgress = reader.ReadFloat(),
                        TotalProgress = reader.ReadFloat(),
                    };
                }
            ),
            TypeSerializer.ForComponent<Processor>(
                (writer, v) =>
                {
                    writer.Write(v.SourceFile.Name);
                    writer.Write(v.SourceFile.Source.ConnectionId.Value);
                    writer.WriteUnsafe(v.Registers);
                    writer.WriteUnsafe(v.Memory.Memory);
                    writer.Write(v.Crash);
                    writer.Write(v.Signal);
                    writer.Write(v.SignalNotified);
                    writer.Write(v.IncomingTransmissions, (writer, v) =>
                    {
                        writer.WriteUnsafe(v.Data);
                        writer.WriteUnsafe(v.Metadata);
                    });
                    writer.Write(v.OutgoingTransmissions, (writer, v) =>
                    {
                        writer.WriteUnsafe(v.Data);
                        writer.WriteUnsafe(v.Metadata);
                    });
                    writer.Write(v.CommandQueue, (writer, v) =>
                    {
                        writer.WriteUnsafe(v.Id);
                        writer.WriteUnsafe(v.Data);
                    });
                    writer.Write(v.IsKeyRequested);
                    writer.WriteUnsafe(v.InputKey);
                    writer.Write(v.RadarRequest);
                    writer.WriteUnsafe(v.RadarResponse);
                    writer.Write(v.StdOutBufferCursor);
                    writer.WriteUnsafe(v.StdOutBuffer);
                },
                (BinaryReader reader, ref Processor v) =>
                {
                    v.SourceFile.Name = reader.ReadFixedString128();
                    v.SourceFile.Source.ConnectionId.Value = reader.ReadInt();
                    v.Registers = reader.ReadUnsafe<LanguageCore.Runtime.Registers>();
                    v.Memory.Memory = reader.ReadUnsafe<FixedBytes2048>();
                    v.Crash = reader.ReadInt();
                    v.Signal = (LanguageCore.Runtime.Signal)reader.ReadByte();
                    v.SignalNotified = reader.ReadBool();
                    v.IncomingTransmissions = reader.ReadFixedList128<BufferedUnitTransmission>(reader =>
                    {
                        return new BufferedUnitTransmission()
                        {
                            Data = reader.ReadFixedList32Unsafe<byte>(),
                            Metadata = reader.ReadUnsafe<IncomingUnitTransmissionMetadata>(),
                        };
                    });
                    v.OutgoingTransmissions = reader.ReadFixedList128<BufferedUnitTransmissionOutgoing>(reader =>
                    {
                        return new BufferedUnitTransmissionOutgoing()
                        {
                            Data = reader.ReadFixedList32Unsafe<byte>(),
                            Metadata = reader.ReadUnsafe<OutgoingUnitTransmissionMetadata>(),
                        };
                    });
                    v.CommandQueue = reader.ReadFixedList128<UnitCommandRequest>(reader =>
                    {
                        int id = reader.ReadInt();
                        FixedList32Bytes<byte> data = reader.ReadFixedList32Unsafe<byte>();
                        return new UnitCommandRequest(id, data);
                    });
                    v.IsKeyRequested = reader.ReadBool();
                    v.InputKey = reader.ReadFixedList128Unsafe<byte>();
                    v.RadarRequest = reader.ReadFloat2();
                    v.RadarResponse = reader.ReadUnsafe<RadarResponse>();
                    v.StdOutBufferCursor = reader.ReadUlong();
                    v.StdOutBuffer = reader.ReadFixedList512Unsafe<byte>();
                }
            ),
            TypeSerializer.ForDynamicBufferItem<BufferedDamage>(
                (writer, v) =>
                {
                    writer.Write(v.Amount);
                    writer.Write(v.Direction);
                },
                (reader) =>
                {
                    return new BufferedDamage()
                    {
                        Amount = reader.ReadFloat(),
                        Direction = reader.ReadFloat3(),
                    };
                }
            ),
            TypeSerializer.ForDynamicBufferItem<BufferedProducingUnit>(
                (writer, v) =>
                {
                    writer.Write(v.Name);
                },
                (reader) =>
                {
                    FixedString32Bytes name = reader.ReadFixedString32();
                    int i = units.IndexOf(v => v.Name == name);
                    if (i == -1) Debug.LogWarning($"Unit `{name}` not found");
                    return i == -1 ? default : new BufferedProducingUnit()
                    {
                        Name = name,
                        ProductionTime = units[i].ProductionTime,
                        Prefab = units[i].Prefab
                    };
                }
            ),
            TypeSerializer.ForDynamicBufferItem<BufferedTechnologyHashIn>(
                (writer, v) =>
                {
                    writer.WriteUnsafe(v.Hash);
                },
                (reader) =>
                {
                    return new BufferedTechnologyHashIn()
                    {
                        Hash = reader.ReadUnsafe<FixedBytes30>(),
                    };
                }
            ),
            TypeSerializer.ForDynamicBufferItem<BufferedTechnologyHashOut>(
                (writer, v) =>
                {
                    writer.WriteUnsafe(v.Hash);
                },
                (reader) =>
                {
                    return new BufferedTechnologyHashOut()
                    {
                        Hash = reader.ReadUnsafe<FixedBytes30>(),
                    };
                }
            ),
            TypeSerializer.ForDynamicBufferItem<BufferedUnitTransmission>(
                (writer, v) =>
                {
                    writer.WriteUnsafe(v.Data);
                    writer.WriteUnsafe(v.Metadata);
                },
                (reader) =>
                {
                    return new BufferedUnitTransmission()
                    {
                        Data = reader.ReadFixedList32Unsafe<byte>(),
                        Metadata = reader.ReadUnsafe<IncomingUnitTransmissionMetadata>(),
                    };
                }
            ),
            TypeSerializer.ForDynamicBufferItem<BufferedUnitTransmissionOutgoing>(
                (writer, v) =>
                {
                    writer.WriteUnsafe(v.Data);
                    writer.WriteUnsafe(v.Metadata);
                },
                (reader) =>
                {
                    return new BufferedUnitTransmissionOutgoing()
                    {
                        Data = reader.ReadFixedList32Unsafe<byte>(),
                        Metadata = reader.ReadUnsafe<OutgoingUnitTransmissionMetadata>(),
                    };
                }
            ),
            TypeSerializer.ForDynamicBufferItem<BufferedWire>(
                (writer, v) =>
                {
                    writer.Write(v.EntityA);
                    writer.Write(v.EntityB);
                    writer.Write(v.PortA);
                    writer.Write(v.PortB);
                },
                (reader) =>
                {
                    Entity ea = reader.ReadEntityUnsafe();
                    Entity eb = reader.ReadEntityUnsafe();
                    byte pa = reader.ReadByte();
                    byte pb = reader.ReadByte();

                    return new BufferedWire()
                    {
                        EntityA = ea,
                        EntityB = eb,
                        PortA = pa,
                        PortB = pb,
                        GhostA = default,
                        GhostB = default,
                    };
                },
                (ref BufferedWire component, EntityReferenceResolver entityReferenceResolver) =>
                {
                    component.EntityA = entityReferenceResolver(component.EntityA);
                    component.EntityB = entityReferenceResolver(component.EntityB);
                },
                (ref BufferedWire component, GhostReferenceResolver ghostReferenceResolver) =>
                {
                    component.GhostA = ghostReferenceResolver(component.EntityA);
                    component.GhostB = ghostReferenceResolver(component.EntityB);
                }
            ),
        };
    }

    static List<PrefabIdSerializer> GetPrefabInstanceIdSerializers(EntityManager entityManager, List<FixedString128Bytes> serializedPrespawnedEntities)
    {
        DynamicBuffer<BufferedBuilding> buildings = GetSingletonBuffer<BuildingDatabase, BufferedBuilding>(entityManager, false);
        DynamicBuffer<BufferedUnit> units = GetSingletonBuffer<UnitDatabase, BufferedUnit>(entityManager, false);
        DynamicBuffer<BufferedProjectile> projectiles = GetSingletonBuffer<ProjectileDatabase, BufferedProjectile>(entityManager, false);
        PrefabDatabase prefabs = GetSingleton<PrefabDatabase>(entityManager);
        NativeArray<Research>.ReadOnly researches;
        {
            EntityQuery q = entityManager.CreateEntityQuery(typeof(Research));
            researches = q.ToComponentDataArray<Research>(Allocator.Temp).AsReadOnly();
            q.Dispose();
        }

        return new()
        {
            PrefabIdSerializer.ForComponent<CoreComputer>(
                (writer, v) => { },
                (reader, entityManager) =>
                {
                    return entityManager.Instantiate(prefabs.CoreComputer);
                }
            ),
            PrefabIdSerializer.ForComponent<BuildingPrefabInstance>(
                (writer, v) =>
                {
                    if (v.Index == -1) Debug.LogError($"Invalid prefab instance");
                    writer.Write(v.Index);
                },
                (reader, entityManager) =>
                {
                    int index = reader.ReadInt();
                    if (index == -1) Debug.LogError($"Invalid prefab instance");
                    return entityManager.Instantiate(buildings[index].Prefab);
                }
            ),
            PrefabIdSerializer.ForComponent<UnitPrefabInstance>(
                (writer, v) =>
                {
                    if (v.Index == -1) Debug.LogError($"Invalid prefab instance");
                    writer.Write(v.Index);
                },
                (reader, entityManager) =>
                {
                    int index = reader.ReadInt();
                    if (index == -1) Debug.LogError($"Invalid prefab instance");
                    return entityManager.Instantiate(units[index].Prefab);
                }
            ),
            PrefabIdSerializer.ForComponent<SavePrespawnedEntity>(
                (writer, v) =>
                {
                    writer.Write(v.Id);
                },
                (reader, entityManager) =>
                {
                    FixedString128Bytes id = reader.ReadFixedString128();
                    serializedPrespawnedEntities.Add(id);
                    using EntityQuery q = entityManager.CreateEntityQuery(typeof(SavePrespawnedEntity));
                    using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
                    foreach (Entity entity in entities)
                    {
                        FixedString128Bytes other = entityManager.GetComponentData<SavePrespawnedEntity>(entity).Id;
                        if (id == other) return entity;
                    }
                    Debug.LogError($"Invalid serialized prespawned entity {id}");
                    return Entity.Null;
                }
            ),
        };
    }

    public static void Save(World serverWorld, string filename)
    {
        EntityManager entityManager = serverWorld.EntityManager;

        DynamicBuffer<BufferedBuilding> buildings = GetSingletonBuffer<BuildingDatabase, BufferedBuilding>(entityManager, false);
        DynamicBuffer<BufferedUnit> units = GetSingletonBuffer<UnitDatabase, BufferedUnit>(entityManager, false);
        DynamicBuffer<BufferedProjectile> projectiles = GetSingletonBuffer<ProjectileDatabase, BufferedProjectile>(entityManager, false);
        DynamicBuffer<BufferedSpawn> spawns = GetSingletonBuffer<Spawns, BufferedSpawn>(entityManager, false);
        PrefabDatabase prefabs = GetSingleton<PrefabDatabase>(entityManager);

        using BinaryWriter writer = new FileBinaryWriter(filename);

        writer.Write(serverWorld.Unmanaged.GetSystem<PlayerSystemServer>().ServerGuid);

        {
            EntityQuery q = entityManager.CreateEntityQuery(typeof(Player), typeof(RealPlayer));
            NativeArray<Entity> e = q.ToEntityArray(Allocator.Temp);
            writer.Write(e.Length);
            foreach (Entity entity in e)
            {
                Player player = entityManager.GetComponentData<Player>(entity);
                RealPlayer playerR = entityManager.GetComponentData<RealPlayer>(entity);
                DynamicBuffer<BufferedAcquiredResearch> acquiredResearches = entityManager.GetBuffer<BufferedAcquiredResearch>(entity);

                writer.Write(player.Guid);
                writer.Write(playerR.ConnectionState);
                writer.Write(player.InCreative);
                writer.Write(player.IsAdmin);
                writer.Write(player.IsCoreComputerSpawned);
                writer.Write(player.Nickname);
                writer.Write(player.Outcome);
                writer.Write(player.Resources);
                writer.Write(player.Team);

                writer.Write(acquiredResearches, (writer, i) =>
                {
                    writer.Write(i.Name);
                });
            }
            q.Dispose();
        }

        {
            EntityQuery q = entityManager.CreateEntityQuery(typeof(Player), typeof(VirtualPlayer));
            NativeArray<Entity> e = q.ToEntityArray(Allocator.Temp);
            writer.Write(e.Length);
            foreach (Entity entity in e)
            {
                Player player = entityManager.GetComponentData<Player>(entity);
                VirtualPlayer playerV = entityManager.GetComponentData<VirtualPlayer>(entity);
                DynamicBuffer<BufferedAcquiredResearch> acquiredResearches = entityManager.GetBuffer<BufferedAcquiredResearch>(entity);

                writer.Write(player.Guid);
                writer.Write(playerV.Index);
                writer.Write(player.InCreative);
                writer.Write(player.IsAdmin);
                writer.Write(player.IsCoreComputerSpawned);
                writer.Write(player.Nickname);
                writer.Write(player.Outcome);
                writer.Write(player.Resources);
                writer.Write(player.Team);

                writer.Write(acquiredResearches, (writer, i) =>
                {
                    writer.Write(i.Name);
                });
            }
            q.Dispose();
        }

        {
            writer.Write(spawns, (writer, v) =>
            {
                writer.Write(v.IsOccupied);
            });
        }

        {
            FileChunkManagerSystem fileChunkManager = FileChunkManagerSystem.GetInstance(serverWorld);
            writer.Write(fileChunkManager.RemoteFiles, (writer, v) =>
            {
                writer.Write(v.Key.Name);
                writer.Write(v.Key.Source.ConnectionId.Value);
                writer.Write(v.Value.Status);
                writer.Write(v.Value.Data.Data);
                writer.Write(v.Value.Data.Version);
                writer.Write(v.Value.RemotePath);
            });
        }

        {
            CompilerSystemServer compilerSystem = serverWorld.GetExistingSystemManaged<CompilerSystemServer>();
            writer.Write(compilerSystem.CompiledSources, (writer, v) =>
            {
                writer.Write(v.Key.Name);
                writer.Write(v.Key.Source.ConnectionId.Value);

                if (v.Value.Code.HasValue && v.Value.GeneratedFunctions!.Value.Length == 0)
                {
                    writer.Write(true);
                    writer.WriteArray(v.Value.Code.Value);
                    writer.Write(v.Value.CompiledVersion);
                    writer.Write(v.Value.HotReloadVersion);
                    writer.Write(v.Value.IsSuccess);
                    writer.Write(v.Value.LatestVersion);
                    writer.Write((int)v.Value.Status);
                    writer.WriteArray(v.Value.UnitCommandDefinitions!.Value);
                }
                else
                {
                    writer.Write(false);
                }
            });
        }

        List<TypeSerializer> types = GetSerializers(entityManager);
        List<PrefabIdSerializer> prefabTypes = GetPrefabInstanceIdSerializers(entityManager, new());

        NativeList<EntityArchetype> archetypes = new(Allocator.Temp);
        entityManager.GetAllArchetypes(archetypes);

        using NativeArray<ArchetypeChunk> chunks = entityManager.GetAllChunks(Allocator.Temp);

        List<(EntityArchetype Archetype, int PrefabSerializer)> saveableArchetypes = new();
        foreach (EntityArchetype archetype in archetypes)
        {
            if (archetype.Disabled) continue;
            if (archetype.Prefab) continue;

            NativeArray<ComponentType> componentTypes = archetype.GetComponentTypes(Allocator.Temp);

            int prefabIndex = prefabTypes.FindIndex(v => componentTypes.Any(w => v.Is(w)));
            if (prefabIndex == -1) continue;

            if (!chunks.Any(v => v.Archetype == archetype && v.Count > 0)) continue;

            saveableArchetypes.Add((archetype, prefabIndex));
        }

        writer.Write(saveableArchetypes.Count);

        foreach ((EntityArchetype archetype, int prefabIndex) in saveableArchetypes)
        {
            NativeArray<ComponentType> componentTypes = archetype.GetComponentTypes(Allocator.Temp);

            writer.Write(prefabIndex);

            int[] typeIndices = componentTypes.Select(v => types.FindIndex(w => v == w.Type)).Where(v => v != -1).ToArray();
            writer.Write(typeIndices, (w, v) => w.Write(v));

            ArchetypeChunk[] archetypeChunks = chunks.Where(v => v.Archetype == archetype).ToArray();
            int entityCount = archetypeChunks.Sum(v => v.Count);

            writer.Write(entityCount);

            foreach (ArchetypeChunk chunk in archetypeChunks)
            {
                using NativeArray<Entity> entities = chunk.GetNativeArray(entityManager.GetEntityTypeHandle());
                foreach (Entity entity in entities)
                {
                    writer.Write(entity);
                    prefabTypes[prefabIndex].Serializer(writer, entity, entityManager);
                    foreach (int typeIndex in typeIndices)
                    {
                        types[typeIndex].Serializer(writer, entity, entityManager);
                    }
                }
            }
        }

        /*
        using var prespawnedEntityQ = entityManager.CreateEntityQuery(typeof(SavePrespawnedEntity));
        using var prespawnedEntities = prespawnedEntityQ.ToEntityArray(Allocator.Temp);

        writer.Write(prespawnedEntities.Length);

        for (int i = 0; i < prespawnedEntities.Length; i++)
        {
            var entity = prespawnedEntities[i];
            var component = entityManager.GetComponentData<SavePrespawnedEntity>(entity);
            writer.Write(component.Id);
        }
        */
    }

    public static void Load(World serverWorld, EntityCommandBuffer commandBuffer, string filename, out Action? onGhostsSpawned)
    {
        EntityManager entityManager = serverWorld.EntityManager;

        DynamicBuffer<BufferedBuilding> buildings = GetSingletonBuffer<BuildingDatabase, BufferedBuilding>(entityManager, false);
        DynamicBuffer<BufferedUnit> units = GetSingletonBuffer<UnitDatabase, BufferedUnit>(entityManager, false);
        DynamicBuffer<BufferedProjectile> projectiles = GetSingletonBuffer<ProjectileDatabase, BufferedProjectile>(entityManager, false);
        DynamicBuffer<BufferedSpawn> spawns = GetSingletonBuffer<Spawns, BufferedSpawn>(entityManager, false);
        PrefabDatabase prefabs = GetSingleton<PrefabDatabase>(entityManager);

        using BinaryReader reader = new FileBinaryReader(filename);

        serverWorld.Unmanaged.GetSystem<PlayerSystemServer>().ServerGuid = reader.ReadGuid();

        {
            int playerCount = reader.ReadInt();
            for (int i = 0; i < playerCount; i++)
            {
                Entity newPlayer = commandBuffer.Instantiate(prefabs.Player);
                Player player = new()
                {
                    Team = Player.UnassignedTeam,
                    IsCoreComputerSpawned = false,
                    Guid = default,
                    Nickname = default,
                };
                RealPlayer playerR = new()
                {
                    ConnectionId = -1,
                    ConnectionState = PlayerConnectionState.Disconnected,
                };

                player.Guid = reader.ReadGuid();
                PlayerConnectionState connectionState = (PlayerConnectionState)reader.ReadByte();
                if (connectionState is PlayerConnectionState.Connected) connectionState = PlayerConnectionState.Disconnected;
                if (connectionState is PlayerConnectionState.Local or PlayerConnectionState.Server) playerR.ConnectionId = 0;
                playerR.ConnectionState = connectionState;
                player.InCreative = reader.ReadBool();
                player.IsAdmin = reader.ReadBool();
                player.IsCoreComputerSpawned = reader.ReadBool();
                player.Nickname = reader.ReadFixedString32();
                player.Outcome = (GameOutcome)reader.ReadByte();
                player.Resources = reader.ReadFloat();
                player.Team = reader.ReadInt();

                commandBuffer.SetComponent(newPlayer, player);
                commandBuffer.SetComponent(newPlayer, playerR);

                reader.ReadDynamicBuffer(commandBuffer.SetBuffer<BufferedAcquiredResearch>(newPlayer), reader =>
                {
                    BufferedAcquiredResearch res = default;
                    res.Name = reader.ReadFixedString64();
                    return res;
                });
            }
        }

        {
            int playerCount = reader.ReadInt();
            for (int i = 0; i < playerCount; i++)
            {
                Entity newPlayer = commandBuffer.Instantiate(prefabs.VirtualPlayer);
                Player player = new()
                {
                    Team = Player.UnassignedTeam,
                    IsCoreComputerSpawned = false,
                    Guid = default,
                    Nickname = default,
                };
                VirtualPlayer playerV = new();

                player.Guid = reader.ReadGuid();
                playerV.Index = reader.ReadInt();
                player.InCreative = reader.ReadBool();
                player.IsAdmin = reader.ReadBool();
                player.IsCoreComputerSpawned = reader.ReadBool();
                player.Nickname = reader.ReadFixedString32();
                player.Outcome = (GameOutcome)reader.ReadByte();
                player.Resources = reader.ReadFloat();
                player.Team = reader.ReadInt();

                commandBuffer.SetComponent(newPlayer, player);
                commandBuffer.SetComponent(newPlayer, playerV);

                reader.ReadDynamicBuffer(commandBuffer.SetBuffer<BufferedAcquiredResearch>(newPlayer), reader =>
                {
                    BufferedAcquiredResearch res = default;
                    res.Name = reader.ReadFixedString64();
                    return res;
                });
            }
        }

        {
            reader.ReadDynamicBuffer<BufferedSpawn>(spawns, (BinaryReader reader, ref BufferedSpawn item) =>
            {
                item.IsOccupied = reader.ReadBool();
            });
        }

        {
            FileChunkManagerSystem fileChunkManager = FileChunkManagerSystem.GetInstance(serverWorld);
            foreach (KeyValuePair<FileId, RemoteFile> item in reader.ReadArray((reader) =>
            {
                FileId key = new(reader.ReadFixedString128(), new NetcodeEndPoint(new Unity.NetCode.NetworkId() { Value = reader.ReadInt() }, Entity.Null));
                RemoteFile value = new((FileResponseStatus)reader.ReadInt(), new FileData(reader.ReadArray(v => v.ReadByte()), reader.ReadLong()), key, reader.ReadFixedString128());
                return new KeyValuePair<FileId, RemoteFile>(key, value);
            }))
            {
                fileChunkManager.RemoteFiles.Add(item.Key, item.Value);
            }
        }

        CompilerSystemServer compilerSystem = serverWorld.GetExistingSystemManaged<CompilerSystemServer>();
        foreach (FileId source in reader.ReadArray((reader) =>
        {
            return new FileId(reader.ReadFixedString128(), new NetcodeEndPoint(new Unity.NetCode.NetworkId() { Value = reader.ReadInt() }, Entity.Null));
        }))
        {
            bool ok = reader.ReadBool();
            var v = new CompiledSourceServer(
                source,
                default,
                1,
                1,
                CompilationStatus.Secuedued,
                0,
                false,
                default,
                default,
                default,
                new LanguageCore.DiagnosticsCollection()
            );

            if (ok)
            {
                v.Code = reader.ReadNativeArray<LanguageCore.Runtime.Instruction>(Allocator.Persistent);
                v.CompiledVersion = reader.ReadLong();
                v.HotReloadVersion = reader.ReadLong();
                v.IsSuccess = reader.ReadBool();
                v.LatestVersion = reader.ReadLong();
                v.Status = (CompilationStatus)reader.ReadInt();
                v.UnitCommandDefinitions = reader.ReadNativeArray<UnitCommandDefinition>(Allocator.Persistent);
            }

            compilerSystem.CompiledSources.Add(source, v);
        }

        Dictionary<Entity, Entity> deserializedEntities = new();
        List<FixedString128Bytes> serializedPrespawnedEntities = new();
        List<TypeSerializer> types = GetSerializers(entityManager);
        List<PrefabIdSerializer> prefabTypes = GetPrefabInstanceIdSerializers(entityManager, serializedPrespawnedEntities);

        List<(Entity Entity, EntityReferencePopulatorSimple EntityReferencePopulator)> entityReferencePopulators = new();
        List<(Entity Entity, GhostReferencePopulatorSimple GhostReferencePopulator)> ghostReferencePopulators = new();

        int saveableArchetypesCount = reader.ReadInt();

        for (int i = 0; i < saveableArchetypesCount; i++)
        {
            int prefabIndex = reader.ReadInt();

            int[] typeIndices = reader.ReadArray(static v => v.ReadInt());

            int entityCount = reader.ReadInt();

            for (int j = 0; j < entityCount; j++)
            {
                Entity serialized = reader.ReadEntityUnsafe();

                Entity entity = prefabTypes[prefabIndex].Deserializer(reader, entityManager);
                deserializedEntities.Add(serialized, entity);

                foreach (int typeIndex in typeIndices)
                {
                    types[typeIndex].Deserializer(reader, entity, entityManager);
                    if (types[typeIndex].EntityReferencePopulator is not null)
                    {
                        entityReferencePopulators.Add((entity, types[typeIndex].EntityReferencePopulator!));
                    }
                    if (types[typeIndex].GhostReferencePopulator is not null)
                    {
                        ghostReferencePopulators.Add((entity, types[typeIndex].GhostReferencePopulator!));
                    }
                }
            }
        }

        /*
        int prespawnedEntitiesCount = reader.ReadInt();

        using var prespawnedEntityQ = entityManager.CreateEntityQuery(typeof(SavePrespawnedEntity));
        using var prespawnedEntities = prespawnedEntityQ.ToEntityArray(Allocator.Temp);

        List<Entity> savedPrespawnedEntities = new();

        for (int i = 0; i < prespawnedEntitiesCount; i++)
        {
            var id = reader.ReadFixedString128();

            for (int j = 0; j < prespawnedEntities.Length; j++)
            {
                var entity = prespawnedEntities[j];
                var component = entityManager.GetComponentData<SavePrespawnedEntity>(entity);
                if (id == component.Id)
                {
                    savedPrespawnedEntities.Add(entity);
                    break;
                }
            }
        }
        */

        {
            using EntityQuery q = entityManager.CreateEntityQuery(typeof(SavePrespawnedEntity));
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                FixedString128Bytes other = entityManager.GetComponentData<SavePrespawnedEntity>(entity).Id;
                if (!serializedPrespawnedEntities.Any(v => v == other))
                {
                    if (deserializedEntities.ContainsKey(entity)) Debug.LogError($"Prespawned entity is being destroyed");
                    entityManager.DestroyEntity(entity);
                }
            }
        }

        Entity EntityReferenceResolver(Entity entity)
        {
            if (entity == Entity.Null) return Entity.Null;
            if (deserializedEntities.TryGetValue(entity, out Entity res)) return res;
            Debug.LogError($"Referenced entity was not serialized");
            return Entity.Null;
        }

        foreach ((Entity entity, EntityReferencePopulatorSimple entityReferencePopulator) in entityReferencePopulators)
        {
            entityReferencePopulator(entity, entityManager, EntityReferenceResolver);
        }

        SpawnedGhost GhostReferenceResolver(Entity entity)
        {
            if (entity == Entity.Null) return default;
            GhostInstance ghost = entityManager.GetComponentData<GhostInstance>(entity);
            if (!ghost.spawnTick.IsValid)
            {
                Debug.LogError($"Ghost not spawned yet");
                return default;
            }
            return ghost;
        }

        onGhostsSpawned = ghostReferencePopulators.Count == 0 ? null : () =>
        {
            foreach ((Entity entity, GhostReferencePopulatorSimple ghostReferencePopulator) in ghostReferencePopulators)
            {
                ghostReferencePopulator(entity, entityManager, GhostReferenceResolver);
            }
        };

        while (compilerSystem.CompiledSources.Any(v => v.Value.Status != CompilationStatus.Done))
        {
            serverWorld.Update();
        }
    }
}

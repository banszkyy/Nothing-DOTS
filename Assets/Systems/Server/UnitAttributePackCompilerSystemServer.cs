using System;
using System.Collections.Immutable;
using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial class UnitAttributePackCompilerSystemServer : SystemBase
{
    const bool EnableLogging = true;

    delegate void AttributeCompiler(Entity entity, ref UnitAttributesPackBuilder builder);
    delegate void AttributeCompiler<T>(T component, ref UnitAttributesPackBuilder builder) where T : unmanaged, IComponentData;

    readonly struct UnitAttributesPackBuilder : IDisposable
    {
        readonly NativeList<AttributeMeta> Fields;
        readonly NativeList<byte> Data;

        public UnitAttributesPackBuilder(Allocator allocator)
        {
            Fields = new NativeList<AttributeMeta>(0, allocator);
            Data = new NativeList<byte>(0, allocator);
        }

        public unsafe void Add<T>(T data) where T : unmanaged
        {
            int offset = Data.Length;
            int size = sizeof(T);
            byte* dataPtr = (byte*)(void*)&data;

            Fields.Add(new AttributeMeta(checked((byte)offset), checked((byte)size)));
            Data.AddRange(dataPtr, size);
        }

        public void Add()
        {
            Fields.Add(default);
        }

        public void Dispose()
        {
            Fields.Dispose();
            Data.Dispose();
        }

        public unsafe UnitAttributesPack AsReadOnly()
        {
            return new UnitAttributesPack()
            {
                Fields = Fields.GetUnsafeList()->AsReadOnly(),
                Data = Data.GetUnsafeList()->AsReadOnly(),
            };
        }

        public bool Equals(UnitAttributesPackBuilder attributes)
        {
            if (Fields.Length != attributes.Fields.Length) return false;
            if (Data.Length != attributes.Data.Length) return false;

            for (int i = 0; i < Fields.Length; i++)
            {
                if (Fields[i] != attributes.Fields[i]) return false;
            }

            for (int i = 0; i < Data.Length; i++)
            {
                if (Data[i] != attributes.Data[i]) return false;
            }

            return true;
        }
    }

    ImmutableArray<AttributeCompiler> AttributeCompilers;
    NativeList<UnitAttributesPackBuilder> UnitAttributes;

    static AttributeCompiler CreateAttributeCompiler<T>(EntityManager entityManager, int attributeCount, AttributeCompiler<T> compiler) where T : unmanaged, IComponentData
        => (Entity entity, ref UnitAttributesPackBuilder builder) =>
    {
        if (!entityManager.HasComponent<T>(entity))
        {
            for (int i = 0; i < attributeCount; i++)
            {
                builder.Add();
            }
        }
        else
        {
            compiler(entityManager.GetComponentData<T>(entity), ref builder);
        }
    };

    protected override void OnCreate()
    {
        AttributeCompilers = ImmutableArray.Create(
            CreateAttributeCompiler(EntityManager, 3, (CombatTurret turret, ref UnitAttributesPackBuilder builder) =>
            {
                builder.Add(turret.MagazineSize);
                using EntityQuery q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<BufferedProjectile>());
                BufferedProjectile projectile = q.GetSingletonBuffer<BufferedProjectile>(true)[turret.Projectile];
                builder.Add(projectile.Speed);
                builder.Add(projectile.Damage);
            })
        );
        UnitAttributes = new NativeList<UnitAttributesPackBuilder>(Allocator.Persistent);
    }

    protected override void OnUpdate()
    {
        foreach (var (processor, entity) in
            SystemAPI.Query<RefRW<Processor>>()
            .WithEntityAccess())
        {
            if (processor.ValueRO.Attributes.Fields.IsCreated) continue;

            if (EnableLogging) Debug.Log($"{DebugEx.Prefix(World.Unmanaged)} Compiling unit attributes");

            UnitAttributesPackBuilder builder = new(Allocator.Persistent);
            foreach (AttributeCompiler compiler in AttributeCompilers)
            {
                compiler(entity, ref builder);
            }

            for (int i = 0; i < UnitAttributes.Length; i++)
            {
                if (!UnitAttributes[i].Equals(builder)) continue;

                processor.ValueRW.Attributes = UnitAttributes[i].AsReadOnly();
                builder.Dispose();
                if (EnableLogging) Debug.Log($"{DebugEx.Prefix(World.Unmanaged)} Using cached unit attributes {i}");

                goto ok;
            }

            processor.ValueRW.Attributes = builder.AsReadOnly();
            UnitAttributes.Add(builder);

        ok:;
        }
    }
}

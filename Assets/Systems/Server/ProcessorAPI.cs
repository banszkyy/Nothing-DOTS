#define _UNITY_PROFILER

#if UNITY_EDITOR && EDITOR_DEBUG
#define DEBUG_LINES
#endif

using System;
using AOT;
using LanguageCore.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;
using FunctionScope = ProcessorSystemServer.FunctionScope;

[BurstCompile]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter")]
static unsafe class ProcessorAPI
{
    [BurstCompile]
    public static void GenerateExternalFunctions(ref NativeList<ExternalFunctionScopedSync> buffer)
    {
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(IO.StdOut).Value, IO.Prefix + 1, ExternalFunctionGenerator.SizeOf<byte>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(IO.StdIn).Value, IO.Prefix + 2, 0, ExternalFunctionGenerator.SizeOf<byte>(), default));

        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Math.Sqrt).Value, Math.Prefix + 1, ExternalFunctionGenerator.SizeOf<float>(), ExternalFunctionGenerator.SizeOf<float>(), default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Math.Atan2).Value, Math.Prefix + 2, ExternalFunctionGenerator.SizeOf<float, float>(), ExternalFunctionGenerator.SizeOf<float>(), default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Math.Sin).Value, Math.Prefix + 3, ExternalFunctionGenerator.SizeOf<float>(), ExternalFunctionGenerator.SizeOf<float>(), default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Math.Cos).Value, Math.Prefix + 4, ExternalFunctionGenerator.SizeOf<float>(), ExternalFunctionGenerator.SizeOf<float>(), default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Math.Tan).Value, Math.Prefix + 5, ExternalFunctionGenerator.SizeOf<float>(), ExternalFunctionGenerator.SizeOf<float>(), default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Math.Asin).Value, Math.Prefix + 6, ExternalFunctionGenerator.SizeOf<float>(), ExternalFunctionGenerator.SizeOf<float>(), default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Math.Acos).Value, Math.Prefix + 7, ExternalFunctionGenerator.SizeOf<float>(), ExternalFunctionGenerator.SizeOf<float>(), default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Math.Atan).Value, Math.Prefix + 8, ExternalFunctionGenerator.SizeOf<float>(), ExternalFunctionGenerator.SizeOf<float>(), default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Math.Random).Value, Math.Prefix + 9, 0, ExternalFunctionGenerator.SizeOf<int>(), default));

        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(WirelessTransmission.Send).Value, WirelessTransmission.Prefix + 1, ExternalFunctionGenerator.SizeOf<int, int, float, float>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(WirelessTransmission.Receive).Value, WirelessTransmission.Prefix + 2, ExternalFunctionGenerator.SizeOf<int, int, int>(), ExternalFunctionGenerator.SizeOf<int>(), default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(WiredTransmission.Send).Value, WirelessTransmission.Prefix + 3, ExternalFunctionGenerator.SizeOf<int, int, byte>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(WiredTransmission.Receive).Value, WirelessTransmission.Prefix + 4, ExternalFunctionGenerator.SizeOf<int, int, byte>(), ExternalFunctionGenerator.SizeOf<int>(), default));

        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Sensors.Radar).Value, Sensors.Prefix + 1, 0, 0, default));

        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Environment.ToGlobal).Value, Environment.Prefix + 1, ExternalFunctionGenerator.SizeOf<int>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Environment.ToLocal).Value, Environment.Prefix + 2, ExternalFunctionGenerator.SizeOf<int>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Environment.ToGlobalD).Value, Environment.Prefix + 3, ExternalFunctionGenerator.SizeOf<int>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Environment.ToLocalD).Value, Environment.Prefix + 4, ExternalFunctionGenerator.SizeOf<int>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Environment.Time).Value, Environment.Prefix + 5, 0, ExternalFunctionGenerator.SizeOf<float>(), default));

        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Debug.Line).Value, Debug.Prefix + 1, ExternalFunctionGenerator.SizeOf<float3, byte>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Debug.LineL).Value, Debug.Prefix + 2, ExternalFunctionGenerator.SizeOf<float3, byte>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Debug.Label).Value, Debug.Prefix + 3, ExternalFunctionGenerator.SizeOf<float3, int>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Debug.LabelL).Value, Debug.Prefix + 4, ExternalFunctionGenerator.SizeOf<float3, int>(), 0, default));

        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Commands.Dequeue).Value, Commands.Prefix + 1, ExternalFunctionGenerator.SizeOf<int>(), ExternalFunctionGenerator.SizeOf<int>(), default));

        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(GUI.Create).Value, GUI.Prefix + 1, ExternalFunctionGenerator.SizeOf<int>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(GUI.Destroy).Value, GUI.Prefix + 2, ExternalFunctionGenerator.SizeOf<int>(), 0, default));
        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(GUI.Update).Value, GUI.Prefix + 3, ExternalFunctionGenerator.SizeOf<int>(), 0, default));

        buffer.Add(new((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)BurstCompiler.CompileFunctionPointer<ExternalFunctionUnity>(Attributes.QueryAttribute).Value, Attributes.Prefix + 1, ExternalFunctionGenerator.SizeOf<int, int>(), ExternalFunctionGenerator.SizeOf<byte>(), default));
    }

    public static IExternalFunction[] GenerateManagedExternalFunctions() => new IExternalFunction[]
    {
        new ExternalFunctionStub(IO.Prefix + 1,                "stdout", ExternalFunctionGenerator.SizeOf<byte>(), 0),
        new ExternalFunctionStub(IO.Prefix + 2,                "stdin", 0, ExternalFunctionGenerator.SizeOf<byte>()),

        ExternalFunctionSync.Create<float, float>(Math.Prefix + 1,        "sqrt", MathF.Sqrt),
        ExternalFunctionSync.Create<float, float, float>(Math.Prefix + 2, "atan2", MathF.Atan2),
        ExternalFunctionSync.Create<float, float>(Math.Prefix + 3,         "sin", MathF.Sin),
        ExternalFunctionSync.Create<float, float>(Math.Prefix + 4,         "cos", MathF.Cos),
        ExternalFunctionSync.Create<float, float>(Math.Prefix + 5,         "tan", MathF.Tan),
        ExternalFunctionSync.Create<float, float>(Math.Prefix + 6,         "asin", MathF.Asin),
        ExternalFunctionSync.Create<float, float>(Math.Prefix + 7,         "acos", MathF.Acos),
        ExternalFunctionSync.Create<float, float>(Math.Prefix + 8,         "atan", MathF.Atan),
        ExternalFunctionSync.Create<int>(Math.Prefix + 9,                  "random", Math.SharedRandom.NextInt),

        new ExternalFunctionStub(WirelessTransmission.Prefix + 1,      "wsend", ExternalFunctionGenerator.SizeOf<int, int, float, float>(), 0),
        new ExternalFunctionStub(WirelessTransmission.Prefix + 2,      "wreceive", ExternalFunctionGenerator.SizeOf<int, int, int>(), ExternalFunctionGenerator.SizeOf<int>()),
        new ExternalFunctionStub(WirelessTransmission.Prefix + 3,      "send", ExternalFunctionGenerator.SizeOf<int, int, byte>(), 0),
        new ExternalFunctionStub(WirelessTransmission.Prefix + 4,      "receive", ExternalFunctionGenerator.SizeOf<int, int, byte>(), ExternalFunctionGenerator.SizeOf<int>()),

        new ExternalFunctionStub(Sensors.Prefix + 1,           "radar", 0, 0),

        new ExternalFunctionStub(Environment.Prefix + 1,       "toglobal", ExternalFunctionGenerator.SizeOf<int>(), 0),
        new ExternalFunctionStub(Environment.Prefix + 2,       "tolocal", ExternalFunctionGenerator.SizeOf<int>(), 0),
        new ExternalFunctionStub(Environment.Prefix + 3,       "toglobald", ExternalFunctionGenerator.SizeOf<int>(), 0),
        new ExternalFunctionStub(Environment.Prefix + 4,       "tolocald", ExternalFunctionGenerator.SizeOf<int>(), 0),
        new ExternalFunctionStub(Environment.Prefix + 5,       "time", 0, ExternalFunctionGenerator.SizeOf<float>()),

        new ExternalFunctionStub(Debug.Prefix + 1,             "debug", ExternalFunctionGenerator.SizeOf<float3, byte>(), 0),
        new ExternalFunctionStub(Debug.Prefix + 2,             "ldebug", ExternalFunctionGenerator.SizeOf<float3, byte>(), 0),
        new ExternalFunctionStub(Debug.Prefix + 3,             "debug_label", ExternalFunctionGenerator.SizeOf<float3, int>(), 0),
        new ExternalFunctionStub(Debug.Prefix + 4,             "ldebug_label", ExternalFunctionGenerator.SizeOf<float3, int>(), 0),

        new ExternalFunctionStub(Commands.Prefix + 1,          "dequeue_command", ExternalFunctionGenerator.SizeOf<int>(), ExternalFunctionGenerator.SizeOf<int>()),

        new ExternalFunctionStub(GUI.Prefix + 1,               "gui_create", ExternalFunctionGenerator.SizeOf<int>(), 0),
        new ExternalFunctionStub(GUI.Prefix + 2,               "gui_destroy", ExternalFunctionGenerator.SizeOf<int>(), 0),
        new ExternalFunctionStub(GUI.Prefix + 3,               "gui_update", ExternalFunctionGenerator.SizeOf<int>(), 0),

        new ExternalFunctionStub(Attributes.Prefix + 1,        "attributes_query", ExternalFunctionGenerator.SizeOf<int, int>(), ExternalFunctionGenerator.SizeOf<byte>()),
    };

    public const int GlobalPrefix = unchecked((int)0xFFFF0000);

    [BurstCompile]
    public static class Math
    {
        public const int Prefix = 0x00010000;

        public static readonly Unity.Mathematics.Random SharedRandom = Unity.Mathematics.Random.CreateFromIndex(420);

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Atan2(nint scope, nint arguments, nint returnValue)
        {
            (float a, float b) = ExternalFunctionGenerator.TakeParameters<float, float>(arguments);
            float r = math.atan2(a, b);
            returnValue.Set(r);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Sin(nint scope, nint arguments, nint returnValue)
        {
            float a = ExternalFunctionGenerator.TakeParameters<float>(arguments);
            float r = math.sin(a);
            returnValue.Set(r);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Cos(nint scope, nint arguments, nint returnValue)
        {
            float a = ExternalFunctionGenerator.TakeParameters<float>(arguments);
            float r = math.cos(a);
            returnValue.Set(r);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Tan(nint scope, nint arguments, nint returnValue)
        {
            float a = ExternalFunctionGenerator.TakeParameters<float>(arguments);
            float r = math.tan(a);
            returnValue.Set(r);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Asin(nint scope, nint arguments, nint returnValue)
        {
            float a = ExternalFunctionGenerator.TakeParameters<float>(arguments);
            float r = math.asin(a);
            returnValue.Set(r);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Acos(nint scope, nint arguments, nint returnValue)
        {
            float a = ExternalFunctionGenerator.TakeParameters<float>(arguments);
            float r = math.acos(a);
            returnValue.Set(r);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Atan(nint scope, nint arguments, nint returnValue)
        {
            float a = ExternalFunctionGenerator.TakeParameters<float>(arguments);
            float r = math.atan(a);
            returnValue.Set(r);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Sqrt(nint scope, nint arguments, nint returnValue)
        {
            float a = ExternalFunctionGenerator.TakeParameters<float>(arguments);
            float r = math.sqrt(a);
            returnValue.Set(r);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Random(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerRandom.Auto();
#endif

            returnValue.Set(SharedRandom.NextInt());
        }
    }

    [BurstCompile]
    public static class IO
    {
        public const int Prefix = 0x00020000;

        static readonly ProfilerMarker MarkerStdout = new("ProcessorSystemServer.External.stdout");

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void StdOut(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerStdout.Auto();
#endif

            byte output = arguments.To<byte>();
            if (output == '\r') return;
            Processor* p = ((FunctionScope*)_scope)->EntityRef.Processor;
            if (p->StdOutBuffer.Length >= p->StdOutBuffer.Capacity) p->StdOutBuffer.RemoveAt(0);
            p->StdOutBuffer.Add(output);
            p->StdOutBufferCursor++;
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void StdIn(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            // using ProfilerMarker.AutoScope marker = _ExternalMarker_stdout.Auto();
#endif

            ((FunctionScope*)_scope)->EntityRef.Processor->IsKeyRequested = true;
        }
    }

    [BurstCompile]
    public static class WirelessTransmission
    {
        public const int Prefix = 0x00030000;

        static readonly ProfilerMarker MarkerSend = new("ProcessorSystemServer.External.wsend");
        static readonly ProfilerMarker MarkerReceive = new("ProcessorSystemServer.External.wreceive");

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Send(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerSend.Auto();
#endif

            (int bufferPtr, int length, int directionPtr, float angle) = ExternalFunctionGenerator.TakeParameters<int, int, int, float>(arguments);
            if (bufferPtr == 0 || length == 0) return;

            FunctionScope* scope = (FunctionScope*)_scope;

            if (length < 0 || length >= 30)
            {
                // Passed buffer length must be in range [0,30] inclusive
                scope->ProcessorRef.DoCrash();
                return;
            }

            if (bufferPtr < 0 || bufferPtr + length >= Processor.UserMemorySize)
            {
                // Passed buffer pointer is invalid
                scope->ProcessorRef.DoCrash();
                return;
            }

            Span<byte> memory = new(scope->ProcessorRef.Memory, Processor.UserMemorySize);
            float3 direction = angle != 0f && directionPtr > 0 && directionPtr < memory.Length ? memory.Get<float3>(directionPtr) : default;
            float cosAngle = math.abs(math.cos(angle));

            FixedList32Bytes<byte> data = new();
            data.AddRange((byte*)((nint)scope->ProcessorRef.Memory + bufferPtr), length);

            if (scope->EntityRef.Processor->OutgoingTransmissions.Length >= scope->EntityRef.Processor->OutgoingTransmissions.Capacity)
            { scope->EntityRef.Processor->OutgoingTransmissions.RemoveAt(0); }
            scope->EntityRef.Processor->OutgoingTransmissions.Add(new()
            {
                Data = data,
                Metadata = new OutgoingUnitTransmissionMetadata()
                {
                    IsWireless = true,
                    Wireless = new()
                    {
                        Source = scope->EntityRef.LocalTransform.Position,
                        Direction = direction,
                        CosAngle = cosAngle,
                        Angle = angle,
                    },
                },
            });
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Receive(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerReceive.Auto();
#endif

            returnValue.Set(0);

            (int bufferPtr, int length, int directionPtr) = ExternalFunctionGenerator.TakeParameters<int, int, int>(arguments);
            if (bufferPtr == 0 || length <= 0) return;

            FunctionScope* scope = (FunctionScope*)_scope;

            if (bufferPtr < 0 || bufferPtr + length >= Processor.UserMemorySize)
            {
                // Passed buffer pointer is invalid
                scope->ProcessorRef.DoCrash();
                return;
            }

            ref FixedList128Bytes<BufferedUnitTransmission> received = ref scope->EntityRef.Processor->IncomingTransmissions; // scope->EntityManager.GetBuffer<BufferedUnitTransmission>(scope->SourceEntity);
            if (received.Length == 0) return;

            for (int i = 0; i < received.Length; i++)
            {
                BufferedUnitTransmission item = received[i];
                if (!item.Metadata.IsWireless) continue;

                int copyLength = math.min(item.Data.Length, length);

                Buffer.MemoryCopy(((byte*)&item.Data) + 2, (byte*)scope->ProcessorRef.Memory + bufferPtr, copyLength, copyLength);

                if (directionPtr > 0)
                {
                    float3 transformed = math.normalize(scope->EntityRef.LocalTransform.InverseTransformPoint(item.Metadata.Wireless.Source));
                    Span<byte> memory = new(scope->ProcessorRef.Memory, Processor.UserMemorySize);
                    memory.Set(directionPtr, transformed);
                }

                if (copyLength >= item.Data.Length)
                {
                    received.RemoveAt(i);
                }
                else
                {
                    item.Data.RemoveRange(0, copyLength);
                    received[i] = item;
                }

                returnValue.Set(copyLength);
                break;
            }
        }
    }

    [BurstCompile]
    public static class WiredTransmission
    {
        public const int Prefix = 0x00030000;

        static readonly ProfilerMarker MarkerSend = new("ProcessorSystemServer.External.send");
        static readonly ProfilerMarker MarkerReceive = new("ProcessorSystemServer.External.receive");

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Send(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerSend.Auto();
#endif

            FunctionScope* scope = (FunctionScope*)_scope;

            (int bufferPtr, int length, byte port) = ExternalFunctionGenerator.TakeParameters<int, int, byte>(arguments);
            if (bufferPtr == 0 || length == 0) return;

            if (length < 0 || length >= 30)
            {
                // Passed buffer length must be in range [0,30] inclusive
                scope->ProcessorRef.DoCrash();
                return;
            }

            if (bufferPtr < 0 || bufferPtr + length >= Processor.UserMemorySize)
            {
                // Passed buffer pointer is invalid
                scope->ProcessorRef.DoCrash();
                return;
            }

            Span<byte> memory = new(scope->ProcessorRef.Memory, Processor.UserMemorySize);

            FixedList32Bytes<byte> data = new();
            data.AddRange((byte*)((nint)scope->ProcessorRef.Memory + bufferPtr), length);

            if (scope->EntityRef.Processor->OutgoingTransmissions.Length >= scope->EntityRef.Processor->OutgoingTransmissions.Capacity)
            { scope->EntityRef.Processor->OutgoingTransmissions.RemoveAt(0); }
            scope->EntityRef.Processor->OutgoingTransmissions.Add(new()
            {
                Data = data,
                Metadata = new OutgoingUnitTransmissionMetadata()
                {
                    IsWireless = false,
                    Wired = new()
                    {
                        Port = port
                    },
                },
            });
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Receive(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerReceive.Auto();
#endif

            returnValue.Set(0);

            (int bufferPtr, int length, byte port) = ExternalFunctionGenerator.TakeParameters<int, int, byte>(arguments);
            if (bufferPtr == 0 || length <= 0) return;

            FunctionScope* scope = (FunctionScope*)_scope;

            if (bufferPtr < 0 || bufferPtr + length >= Processor.UserMemorySize)
            {
                // Passed buffer pointer is invalid
                scope->ProcessorRef.DoCrash();
                return;
            }

            ref FixedList128Bytes<BufferedUnitTransmission> received = ref scope->EntityRef.Processor->IncomingTransmissions; // scope->EntityManager.GetBuffer<BufferedUnitTransmission>(scope->SourceEntity);
            if (received.Length == 0) return;

            for (int i = 0; i < received.Length; i++)
            {
                BufferedUnitTransmission item = received[i];
                if (item.Metadata.IsWireless) continue;
                if (item.Metadata.Wired.Port != port) continue;

                int copyLength = math.min(item.Data.Length, length);

                Buffer.MemoryCopy(((byte*)&item.Data) + 2, (byte*)scope->ProcessorRef.Memory + bufferPtr, copyLength, copyLength);

                if (copyLength >= item.Data.Length)
                {
                    received.RemoveAt(i);
                }
                else
                {
                    item.Data.RemoveRange(0, copyLength);
                    received[i] = item;
                }

                returnValue.Set(copyLength);
                break;
            }
        }
    }

    [BurstCompile]
    public static class Commands
    {
        public const int Prefix = 0x00040000;

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Dequeue(nint _scope, nint arguments, nint returnValue)
        {
            returnValue.Set(0);

            int dataPtr = ExternalFunctionGenerator.TakeParameters<int>(arguments);
            if (dataPtr == 0) return;

            FunctionScope* scope = (FunctionScope*)_scope;

            if (dataPtr < 0 || dataPtr >= Processor.UserMemorySize)
            {
                // Passed buffer pointer is invalid
                scope->ProcessorRef.DoCrash();
                return;
            }

            ref FixedList128Bytes<UnitCommandRequest> queue = ref scope->EntityRef.Processor->CommandQueue; // scope->EntityManager.GetBuffer<BufferedUnitTransmission>(scope->SourceEntity);
            if (queue.Length == 0) return;

            UnitCommandRequest first = queue[0];
            queue.RemoveAt(0);

            for (int i = 0; i < first.Data.Length; i++)
            {
                *((byte*)scope->ProcessorRef.Memory + dataPtr + i) = first.Data[i];
            }

            returnValue.Set(first.Id);
        }
    }

    [BurstCompile]
    public static class Debug
    {
        public const int Prefix = 0x00050000;

        static readonly ProfilerMarker MarkerDebug = new("ProcessorSystemServer.External.debug");

        const float DebugLineDuration = 1f;

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Line(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerDebug.Auto();
#endif

            (float3 position, byte color) = ExternalFunctionGenerator.TakeParameters<float3, byte>(arguments);

            FunctionScope* scope = (FunctionScope*)_scope;

            if (scope->DebugLines.ListData->Length + 1 < scope->DebugLines.ListData->Capacity) scope->DebugLines.AddNoResize(new(
                scope->EntityRef.Team.Team,
                new BufferedLine(new float3x2(
                    scope->EntityRef.WorldTransform.Position,
                    position
                ), color, default)
            ));

#if DEBUG_LINES_
            DebugEx.DrawLine(
                scope->EntityRef.WorldTransform.Position,
                position,
                new Color(
                    (color & 0b100) != 0 ? 1f : 0f,
                    (color & 0b010) != 0 ? 1f : 0f,
                    (color & 0b001) != 0 ? 1f : 0f
                ),
                DebugLinesClientSystem.Lifetime,
                false);
#endif
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void LineL(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerDebug.Auto();
#endif

            (float3 position, byte color) = ExternalFunctionGenerator.TakeParameters<float3, byte>(arguments);

            FunctionScope* scope = (FunctionScope*)_scope;

            float3 transformed = scope->EntityRef.LocalTransform.TransformPoint(position);

            if (scope->DebugLines.ListData->Length + 1 < scope->DebugLines.ListData->Capacity) scope->DebugLines.AddNoResize(new(
                scope->EntityRef.Team.Team,
                new BufferedLine(new float3x2(
                    scope->EntityRef.WorldTransform.Position,
                    transformed
                ), color, default)
            ));

#if DEBUG_LINES_
            DebugEx.DrawLine(
                scope->EntityRef.WorldTransform.Position,
                transformed,
                new Color(
                    (color & 0b100) != 0 ? 1f : 0f,
                    (color & 0b010) != 0 ? 1f : 0f,
                    (color & 0b001) != 0 ? 1f : 0f
                ),
                DebugLinesClientSystem.Lifetime,
                false);
#endif
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Label(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerDebug.Auto();
#endif

            (float3 position, int textPtr) = ExternalFunctionGenerator.TakeParameters<float3, int>(arguments);

            FunctionScope* scope = (FunctionScope*)_scope;

            FixedString32Bytes text = new();
            for (int i = textPtr; i < textPtr + 32; i += sizeof(char))
            {
                char c = *(char*)((byte*)scope->ProcessorRef.Memory + i);
                if (c == '\0') break;
                text.Append(c);
            }

            if (scope->WorldLabels.ListData->Length + 1 < scope->WorldLabels.ListData->Capacity) scope->WorldLabels.AddNoResize(new(
                scope->EntityRef.Team.Team,
                new BufferedWorldLabel(position, 0b111, text, default)
            ));
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void LabelL(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerDebug.Auto();
#endif

            (float3 position, int textPtr) = ExternalFunctionGenerator.TakeParameters<float3, int>(arguments);

            FunctionScope* scope = (FunctionScope*)_scope;

            FixedString32Bytes text = new();
            for (int i = textPtr; i < textPtr + 32; i += sizeof(char))
            {
                char c = *(char*)((byte*)scope->ProcessorRef.Memory + i);
                if (c == '\0') break;
                text.Append(c);
            }

            float3 transformed = scope->EntityRef.LocalTransform.TransformPoint(position);

            if (scope->WorldLabels.ListData->Length + 1 < scope->WorldLabels.ListData->Capacity) scope->WorldLabels.AddNoResize(new(
                scope->EntityRef.Team.Team,
                new BufferedWorldLabel(transformed, 0b111, text, default)
            ));
        }
    }

    [BurstCompile]
    public static class Environment
    {
        public const int Prefix = 0x00060000;

        static readonly ProfilerMarker MarkerTime = new("ProcessorSystemServer.External.time");
        static readonly ProfilerMarker MarkerToGlobal = new("ProcessorSystemServer.External.toglobal");
        static readonly ProfilerMarker MarkerToLocal = new("ProcessorSystemServer.External.tolocal");

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void ToGlobal(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerToGlobal.Auto();
#endif

            int ptr = ExternalFunctionGenerator.TakeParameters<int>(arguments);
            if (ptr <= 0) return;

            FunctionScope* scope = (FunctionScope*)_scope;
            Span<byte> memory = new(scope->ProcessorRef.Memory, Processor.UserMemorySize);
            float3 point = memory.Get<float3>(ptr);
            float3 transformed = scope->EntityRef.LocalTransform.TransformPoint(point);
            memory.Set(ptr, transformed);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void ToLocal(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerToLocal.Auto();
#endif

            int ptr = ExternalFunctionGenerator.TakeParameters<int>(arguments);
            if (ptr <= 0) return;

            FunctionScope* scope = (FunctionScope*)_scope;
            Span<byte> memory = new(scope->ProcessorRef.Memory, Processor.UserMemorySize);
            float3 point = memory.Get<float3>(ptr);
            float3 transformed = scope->EntityRef.LocalTransform.InverseTransformPoint(point);
            memory.Set(ptr, transformed);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void ToGlobalD(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerToGlobal.Auto();
#endif

            int ptr = ExternalFunctionGenerator.TakeParameters<int>(arguments);
            if (ptr <= 0) return;

            FunctionScope* scope = (FunctionScope*)_scope;
            Span<byte> memory = new(scope->ProcessorRef.Memory, Processor.UserMemorySize);
            float3 direction = memory.Get<float3>(ptr);
            float3 transformed = scope->EntityRef.LocalTransform.TransformDirection(direction);
            memory.Set(ptr, transformed);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void ToLocalD(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerToLocal.Auto();
#endif

            int ptr = ExternalFunctionGenerator.TakeParameters<int>(arguments);
            if (ptr <= 0) return;

            FunctionScope* scope = (FunctionScope*)_scope;
            Span<byte> memory = new(scope->ProcessorRef.Memory, Processor.UserMemorySize);
            float3 direction = memory.Get<float3>(ptr);
            float3 transformed = scope->EntityRef.LocalTransform.InverseTransformDirection(direction);
            memory.Set(ptr, transformed);
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Time(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerTime.Auto();
#endif

            FunctionScope* scope = (FunctionScope*)_scope;
            returnValue.Set(MonoTime.Now);
        }
    }

    [BurstCompile]
    public static class Sensors
    {
        public const int Prefix = 0x00070000;

        static readonly ProfilerMarker MarkerRadar = new("ProcessorSystemServer.External.radar");

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Radar(nint _scope, nint arguments, nint returnValue)
        {
#if UNITY_PROFILER
            using ProfilerMarker.AutoScope marker = MarkerRadar.Auto();
#endif

            FunctionScope* scope = (FunctionScope*)_scope;

            MappedMemory* mapped = (MappedMemory*)((nint)scope->ProcessorRef.Memory + Processor.MappedMemoryStart);

            scope->EntityRef.Processor->RadarResponse = default;
            scope->EntityRef.Processor->RadarRequest = new float2(
                math.cos(mapped->Radar.RadarDirection),
                math.sin(mapped->Radar.RadarDirection)
            );
        }
    }

    [BurstCompile]
    public static class GUI
    {
        public const int Prefix = 0x00080000;

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Create(nint _scope, nint arguments, nint returnValue)
        {
            int _ptr = ExternalFunctionGenerator.TakeParameters<int>(arguments);
            FunctionScope* scope = (FunctionScope*)_scope;

            UserUIElement* ptr = (UserUIElement*)((nint)scope->ProcessorRef.Memory + _ptr);

            int id = 1;
            while (true)
            {
                bool exists = false;

                for (int i = 0; i < scope->UIElements.ListData->Length; i++)
                {
                    if ((*scope->UIElements.ListData)[i].Value.Id != id) continue;
                    if ((*scope->UIElements.ListData)[i].OwnerTeam != scope->EntityRef.Team.Team) continue;
                    exists = true;
                    break;
                }

                if (!exists) break;
                id++;
            }

            UserUIElement element = *ptr;

            element.IsDirty = true;
            element.Id = id;

            scope->UIElements.AddNoResize(new(scope->EntityRef.Team.Team, scope->EntityRef.Entity, *ptr = element));
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Destroy(nint _scope, nint arguments, nint returnValue)
        {
            int id = ExternalFunctionGenerator.TakeParameters<int>(arguments);
            FunctionScope* scope = (FunctionScope*)_scope;

            for (int i = 0; i < scope->UIElements.ListData->Length; i++)
            {
                if ((*scope->UIElements.ListData)[i].Value.Id != id) continue;
                if ((*scope->UIElements.ListData)[i].OwnerTeam != scope->EntityRef.Team.Team) continue;
                (*scope->UIElements.ListData)[i] = default;
                break;
            }
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void Update(nint _scope, nint arguments, nint returnValue)
        {
            int _ptr = ExternalFunctionGenerator.TakeParameters<int>(arguments);
            FunctionScope* scope = (FunctionScope*)_scope;

            UserUIElement* ptr = (UserUIElement*)((nint)scope->ProcessorRef.Memory + _ptr);

            for (int i = 0; i < scope->UIElements.ListData->Length; i++)
            {
                ref EntityOwnedData<UserUIElement> uiElement = ref (*scope->UIElements.ListData).Ptr[i];
                if (uiElement.Value.Id != ptr->Id) continue;
                if (uiElement.OwnerTeam != scope->EntityRef.Team.Team) continue;
                ptr->IsDirty = true;
                uiElement = new EntityOwnedData<UserUIElement>(scope->EntityRef.Team.Team, scope->EntityRef.Entity, *ptr);
                break;
            }
        }
    }

    [BurstCompile]
    public static class Attributes
    {
        public const int Prefix = 0x000A0000;

        [BurstCompile]
        [MonoPInvokeCallback(typeof(ExternalFunctionUnity))]
        public static void QueryAttribute(nint _scope, nint arguments, nint returnValue)
        {
            FunctionScope* scope = (FunctionScope*)_scope;
            (int attributeId, int destination) = ExternalFunctionGenerator.TakeParameters<int, int>(arguments);

            if (destination is < 0 or >= Processor.TotalMemorySize) goto bad;

            UnitAttributesPack attributes = scope->EntityRef.Processor->Attributes;
            if (attributeId < 0 || attributeId >= attributes.Fields.Length) goto bad;

            AttributeMeta attribute = attributes.Fields.Ptr[attributeId];

            scope->ProcessorRef.MemorySpan.Set(destination, new ReadOnlySpan<byte>(attributes.Data.Ptr + attribute.Offset, attribute.Size));

            returnValue.Set(true);
            return;
        bad:
            returnValue.Set(false);
            return;
        }
    }
}

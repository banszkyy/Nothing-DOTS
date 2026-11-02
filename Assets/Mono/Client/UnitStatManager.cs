using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

static class UnitStatManager
{
    public static void Populate(IGeneralUnit unit, VisualElement tooltipContent, IEnumerable<IGeneralUnit> all)
    {
        UnitStatsSchema w = new(tooltipContent);

        float f<TComponent>(TComponent current, Func<TComponent, float> selector, bool baselineIsZero = false)
        {
            float min = baselineIsZero ? 0 : float.MaxValue;
            float max = float.MinValue;
            foreach (IGeneralUnit item in all)
            {
                if (!item.Prefab.TryGetComponent(out TComponent c)) continue;
                float v = selector(c);
                min = MathF.Min(min, v);
                max = MathF.Max(max, v);
            }
            float w = selector(current);
            if (min >= max) return 1f;
            return (w - min) / (max - min);
        }

        w.LabelName.text = unit.Name;

        if (unit.Prefab.TryGetComponent(out DamageableAuthoring damageable))
        {
            w.HealthLabel.text = $"{damageable.MaxHealth}";
            w.HealthBar.value = f(damageable, v => v.MaxHealth, true);
        }
        else
        {
            w.PanelHealth.style.display = DisplayStyle.None;
        }

        if (unit.Prefab.TryGetComponent(out CombatTurretAuthoring combatTurret))
        {
            w.DamageLabel.text = $"{combatTurret.Projectile.Damage}";
            w.DamageBar.value = f(combatTurret, v => v.Projectile.Damage, true);
            w.MagazineLabel.text = $"{combatTurret.MagazineSize}";
            w.ReloadLabel.text = $"{(combatTurret.MagazineSize <= 1 ? Math.Max(combatTurret.MagazineReload, combatTurret.BulletReload) : combatTurret.MagazineReload)}s";
            w.AccuracyLabel.text = $"{MathF.Floor((1f - combatTurret.Spread) * 100f)}%";
            w.AccuracyBar.value = MathF.Pow(1f - combatTurret.Spread, 2f);
        }
        else
        {
            w.PanelCombat.style.display = DisplayStyle.None;
        }

        if (unit.Prefab.TryGetComponent(out BuilderTurretAuthoring builderTurret))
        {
            w.BuilderLabel.text = $"{Builder.BuildSpeed}/s";
            w.BuilderBar.value = 1f;
        }
        else
        {
            w.PanelBuilder.style.display = DisplayStyle.None;
        }

        if (unit.Prefab.TryGetComponent(out RadarAuthoring radar))
        {
            w.RadarLabel.text = $"{Radar.RadarRadius}m";
            w.RadarBar.value = 1f;
        }
        else
        {
            w.PanelRadar.style.display = DisplayStyle.None;
        }

        if (unit.Prefab.TryGetComponent(out VehicleAuthoring vehicle))
        {
            w.VehicleMaxSpeedLabel.text = $"{vehicle.MaxSpeed * 3.6f}km/h";
            w.VehicleMaxSpeedBar.value = f(vehicle, v => v.MaxSpeed, true);
        }
        else
        {
            w.PanelVehicle.style.display = DisplayStyle.None;
        }

        if (unit.Prefab.TryGetComponent(out ProcessorAuthoring processor))
        {
            w.CpuLabel.text = $"{processor.CyclesPerTick * 30f}i/s";
            w.CpuBar.value = f(processor, v => v.CyclesPerTick, true);
            string suffix = "B";
            float mem = processor.MemorySize;
            if (mem >= 1024)
            {
                mem /= 1024;
                suffix = "KiB";
            }
            if (mem >= 1024)
            {
                mem /= 1024;
                suffix = "MiB";
            }
            w.MemoryLabel.text = $"{MathF.Round(mem * 10f) / 10f}{suffix}";
            w.MemoryBar.value = f(processor, v => v.MemorySize, true);
        }
        else
        {
            w.PanelProcessor.style.display = DisplayStyle.None;
        }

        if (unit.Prefab.TryGetComponent(out ConnectorAuthoring connector))
        {
            w.PortsLabel.text = $"x{connector.PortPositions.Length}";
        }
        else
        {
            w.PanelPorts.style.display = DisplayStyle.None;
        }

        if (unit.Prefab.TryGetComponent(out RadioAuthoring radio))
        {
            w.TransmitPowerLabel.text = $"{radio.TxPowerDbm}dBm";
            w.BeamLabel.text = $"{radio.MinBeamHalvings}-{radio.MaxBeamHalvings}";
            w.SectorsLabel.text = $"{radio.BearingSectors}";

            if (radio.RxGainDbi != 0)
            {
                w.ReceiveGainLabel.text = $"{radio.RxGainDbi}dBi";
            }
            else
            {
                w.ReceiveGainIcon.style.display = DisplayStyle.None;
                w.ReceiveGainLabel.style.display = DisplayStyle.None;
            }
        }
        else
        {
            w.PanelRadio.style.display = DisplayStyle.None;
        }

        if (unit.Prefab.TryGetComponent(out RadioJammerAuthoring radioJammer))
        {
            w.JammerLabel.text = $"{radioJammer.EirpDbm}dBm";
            w.JammerBar.value = f(radioJammer, v => v.EirpDbm);
        }
        else
        {
            w.PanelJammer.style.display = DisplayStyle.None;
        }
    }
}

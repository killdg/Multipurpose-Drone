using System;
using NightVisionEffect = BSG.CameraEffects.NightVision;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace Killdg.MultipurposeDrone
{
    internal sealed class DroneNightVision : IDisposable
    {
        // Native GPNVG-18 template. The temporary item only reads the
        // device's optical settings and never enters the player's inventory.
        private const string GpnvgTemplate = "5c0558060db834001b735271";

        private readonly NightVisionEffect effect;
        private readonly bool originalOn;
        private readonly Texture originalMask;
        private readonly Color originalColor;
        private readonly float originalIntensity;
        private readonly float originalNoiseIntensity;
        private readonly float originalNoiseScale;
        private readonly float originalMaskSize;

        private DroneNightVision(NightVisionEffect effect)
        {
            this.effect = effect;
            originalOn = effect.On;
            originalMask = effect.Mask;
            originalColor = effect.Color;
            originalIntensity = effect.Intensity;
            originalNoiseIntensity = effect.NoiseIntensity;
            originalNoiseScale = effect.NoiseScale;
            originalMaskSize = effect.MaskSize;
        }

        internal static DroneNightVision? TryEnable(NightVisionEffect? effect)
        {
            if (effect == null || !effect.isActiveAndEnabled || effect.Noise == null
                || effect.TextureMask == null || !Singleton<ItemFactory>.Instantiated)
            {
                return null;
            }

            var item = Singleton<ItemFactory>.Instance.CreateItem(
                Guid.NewGuid().ToString("N").Substring(0, 24), GpnvgTemplate, null);
            if (item.GetItemComponent<NightVisionComponent>()?.Template is not { } preset)
            {
                return null;
            }

            effect.FastForwardSwitch();
            var lease = new DroneNightVision(effect);
            effect.Color = preset.Color;
            effect.Intensity = preset.Intensity;
            effect.NoiseIntensity = preset.NoiseIntensity;
            effect.NoiseScale = preset.NoiseScale;
            effect.MaskSize = preset.MaskSize;
            effect.SetMask(preset.Mask);
            effect.ApplySettings();
            effect.Switch(true);
            return lease;
        }

        public void Dispose()
        {
            if (effect == null)
            {
                return;
            }

            effect.Color = originalColor;
            effect.Intensity = originalIntensity;
            effect.NoiseIntensity = originalNoiseIntensity;
            effect.NoiseScale = originalNoiseScale;
            effect.MaskSize = originalMaskSize;
            effect.Mask = originalMask;
            effect.ApplySettings();
            effect.Switch(originalOn);
        }
    }
}
